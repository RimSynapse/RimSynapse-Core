"""
RimSynapse Database — SQLite Living Database
Manages colony history, pawn identity, weighted memories, relationships,
interactions, and narrative threads. Self-initializing with schema versioning.
"""

import json
import os
import sqlite3
import threading
import time
from contextlib import contextmanager
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------------------
# Schema Version — bump this when adding migrations
# ---------------------------------------------------------------------------
CURRENT_SCHEMA_VERSION = 1

# ---------------------------------------------------------------------------
# Core Table Definitions (12 tables + schema_registry)
# ---------------------------------------------------------------------------
CORE_TABLES = [
    # -- Tier 1: Identity (always in prompts) --

    """CREATE TABLE IF NOT EXISTS colonies (
        id              INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_name     TEXT NOT NULL,
        faction_name    TEXT,
        biome           TEXT,
        seed            TEXT,
        tile_id         INTEGER,
        scenario        TEXT,
        ideology_name   TEXT,
        year_started    INTEGER,
        created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        last_played_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",

    """CREATE TABLE IF NOT EXISTS pawns (
        id                      INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id               INTEGER NOT NULL REFERENCES colonies(id),
        pawn_id_game            TEXT NOT NULL,
        name_first              TEXT,
        name_nick               TEXT,
        name_last               TEXT,
        gender                  TEXT,
        age_biological          INTEGER,
        age_chronological       INTEGER,
        backstory_childhood     TEXT,
        backstory_childhood_desc TEXT,
        backstory_adulthood     TEXT,
        backstory_adulthood_desc TEXT,
        faction                 TEXT,
        title                   TEXT,
        ideology                TEXT,
        xenotype                TEXT,
        pawn_kind               TEXT,
        is_alive                BOOLEAN DEFAULT 1,
        created_at              TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at              TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(colony_id, pawn_id_game)
    )""",

    """CREATE TABLE IF NOT EXISTS pawn_traits (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        pawn_id     INTEGER NOT NULL REFERENCES pawns(id) ON DELETE CASCADE,
        trait_def   TEXT NOT NULL,
        label       TEXT NOT NULL,
        degree      INTEGER DEFAULT 0,
        description TEXT
    )""",

    """CREATE TABLE IF NOT EXISTS pawn_skills (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        pawn_id     INTEGER NOT NULL REFERENCES pawns(id) ON DELETE CASCADE,
        skill_name  TEXT NOT NULL,
        level       INTEGER DEFAULT 0,
        passion     TEXT DEFAULT 'None',
        UNIQUE(pawn_id, skill_name)
    )""",

    # -- Tier 2: Weighted Memory --

    """CREATE TABLE IF NOT EXISTS memories (
        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id           INTEGER NOT NULL REFERENCES colonies(id),
        pawn_id             INTEGER REFERENCES pawns(id),
        memory_type         TEXT NOT NULL,
        summary             TEXT NOT NULL,
        participants        TEXT,
        tags                TEXT,
        game_tick           INTEGER,
        in_game_date        TEXT,
        weight              REAL DEFAULT 0.8,
        base_weight         REAL DEFAULT 0.8,
        decay_rate          REAL DEFAULT 0.05,
        times_referenced    INTEGER DEFAULT 0,
        created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        last_referenced_at  TIMESTAMP,
        last_decayed_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",

    """CREATE TABLE IF NOT EXISTS relationships (
        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id           INTEGER NOT NULL REFERENCES colonies(id),
        pawn_a_id           INTEGER NOT NULL REFERENCES pawns(id),
        pawn_b_id           INTEGER NOT NULL REFERENCES pawns(id),
        relation_type       TEXT,
        opinion_a_to_b      INTEGER DEFAULT 0,
        opinion_b_to_a      INTEGER DEFAULT 0,
        integral_a_to_b     REAL DEFAULT 0.0,
        integral_b_to_a     REAL DEFAULT 0.0,
        integral_samples    INTEGER DEFAULT 0,
        peak_high_a_to_b    INTEGER DEFAULT 0,
        peak_low_a_to_b     INTEGER DEFAULT 0,
        peak_high_b_to_a    INTEGER DEFAULT 0,
        peak_low_b_to_a     INTEGER DEFAULT 0,
        arc_phase           TEXT DEFAULT 'stable',
        interaction_count   INTEGER DEFAULT 0,
        weight              REAL DEFAULT 0.5,
        last_referenced_at  TIMESTAMP,
        updated_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(colony_id, pawn_a_id, pawn_b_id)
    )""",

    """CREATE TABLE IF NOT EXISTS opinion_samples (
        id              INTEGER PRIMARY KEY AUTOINCREMENT,
        relationship_id INTEGER NOT NULL REFERENCES relationships(id) ON DELETE CASCADE,
        opinion_a_to_b  INTEGER,
        opinion_b_to_a  INTEGER,
        game_tick       INTEGER,
        sampled_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",

    # -- Tier 2: Interactions --

    """CREATE TABLE IF NOT EXISTS interactions (
        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id           INTEGER NOT NULL REFERENCES colonies(id),
        memory_id           INTEGER REFERENCES memories(id),
        player_pawn_id      INTEGER REFERENCES pawns(id),
        npc_pawn_id         INTEGER REFERENCES pawns(id),
        npc_name            TEXT,
        npc_faction         TEXT,
        interaction_type    TEXT NOT NULL,
        situation_summary   TEXT,
        outcome             TEXT,
        outcome_details     TEXT,
        opinion_delta       INTEGER DEFAULT 0,
        relationship_delta  INTEGER DEFAULT 0,
        extracted_keywords  TEXT,
        narrative_summary   TEXT,
        game_tick           INTEGER,
        started_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        ended_at            TIMESTAMP
    )""",

    """CREATE TABLE IF NOT EXISTS interaction_messages (
        id              INTEGER PRIMARY KEY AUTOINCREMENT,
        interaction_id  INTEGER NOT NULL REFERENCES interactions(id) ON DELETE CASCADE,
        turn_number     INTEGER NOT NULL,
        role            TEXT NOT NULL,
        content         TEXT NOT NULL,
        sentiment       REAL,
        mood_shift      INTEGER DEFAULT 0,
        created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",

    """CREATE TABLE IF NOT EXISTS narrative_threads (
        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id           INTEGER NOT NULL REFERENCES colonies(id),
        keyword             TEXT NOT NULL,
        category            TEXT,
        description         TEXT NOT NULL,
        source_interaction_id INTEGER REFERENCES interactions(id),
        source_memory_id    INTEGER REFERENCES memories(id),
        times_referenced    INTEGER DEFAULT 0,
        is_resolved         BOOLEAN DEFAULT 0,
        resolution_summary  TEXT,
        weight              REAL DEFAULT 0.6,
        decay_rate          REAL DEFAULT 0.03,
        created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        last_referenced_at  TIMESTAMP
    )""",

    # -- Tier 3: Operational --

    """CREATE TABLE IF NOT EXISTS prompt_log (
        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
        colony_id           INTEGER NOT NULL REFERENCES colonies(id),
        request_type        TEXT NOT NULL,
        pawn_id             INTEGER REFERENCES pawns(id),
        prompt_text         TEXT,
        response_text       TEXT,
        memory_ids_used     TEXT,
        tokens_used         INTEGER,
        duration_ms         INTEGER,
        game_tick           INTEGER,
        created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",

    # -- Schema Extension Registry --

    """CREATE TABLE IF NOT EXISTS schema_registry (
        mod_id      TEXT PRIMARY KEY,
        version     INTEGER DEFAULT 1,
        table_names TEXT,
        registered_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )""",
]

# Indexes created after tables
CORE_INDEXES = [
    "CREATE INDEX IF NOT EXISTS idx_memories_colony_weight ON memories(colony_id, weight DESC)",
    "CREATE INDEX IF NOT EXISTS idx_memories_pawn_weight ON memories(pawn_id, weight DESC)",
    "CREATE INDEX IF NOT EXISTS idx_memories_type ON memories(memory_type)",
    "CREATE INDEX IF NOT EXISTS idx_opinion_samples_rel ON opinion_samples(relationship_id, game_tick DESC)",
    "CREATE INDEX IF NOT EXISTS idx_interaction_msgs ON interaction_messages(interaction_id, turn_number)",
    "CREATE INDEX IF NOT EXISTS idx_narrative_keywords ON narrative_threads(colony_id, keyword)",
    "CREATE INDEX IF NOT EXISTS idx_narrative_weight ON narrative_threads(colony_id, weight DESC)",
    "CREATE INDEX IF NOT EXISTS idx_relationships_pawn ON relationships(pawn_a_id, weight DESC)",
]

# ---------------------------------------------------------------------------
# Migrations — keyed by target version number
# ---------------------------------------------------------------------------
MIGRATIONS = {
    # Version 1 is the initial schema (created by CORE_TABLES above)
    # Future migrations go here:
    # 2: [
    #     "ALTER TABLE pawns ADD COLUMN some_new_column TEXT",
    #     "CREATE TABLE IF NOT EXISTS some_new_table (...)",
    # ],
}


# ---------------------------------------------------------------------------
# Database Manager
# ---------------------------------------------------------------------------
class RimSynapseDB:
    """Thread-safe SQLite database manager with schema versioning."""

    def __init__(self, db_path: str = None, log_func=None):
        """
        Initialize the database manager.

        Args:
            db_path: Path to the SQLite database file. Defaults to ./data/rimsynapse.db
            log_func: Optional logging function (level, type, message, details)
        """
        if db_path is None:
            db_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "rimsynapse.db")

        self.db_path = db_path
        self.log = log_func or (lambda *a, **kw: None)
        self._local = threading.local()

        # Ensure directory exists
        os.makedirs(os.path.dirname(os.path.abspath(self.db_path)), exist_ok=True)

        # Initialize schema
        self._init_schema()

    @contextmanager
    def _get_conn(self):
        """Get a thread-local database connection."""
        if not hasattr(self._local, 'conn') or self._local.conn is None:
            self._local.conn = sqlite3.connect(self.db_path)
            self._local.conn.row_factory = sqlite3.Row
            self._local.conn.execute("PRAGMA journal_mode=WAL")
            self._local.conn.execute("PRAGMA foreign_keys=ON")
        yield self._local.conn

    def _init_schema(self):
        """Initialize core tables and run any pending migrations."""
        is_new = not os.path.exists(self.db_path) or os.path.getsize(self.db_path) == 0

        with self._get_conn() as conn:
            cursor = conn.cursor()

            # Create all core tables
            for sql in CORE_TABLES:
                cursor.execute(sql)

            # Create all indexes
            for sql in CORE_INDEXES:
                cursor.execute(sql)

            conn.commit()

            # Check schema version
            current_version = cursor.execute("PRAGMA user_version").fetchone()[0]

            if is_new or current_version == 0:
                # Fresh database — set to current version
                cursor.execute(f"PRAGMA user_version = {CURRENT_SCHEMA_VERSION}")
                conn.commit()
                self.log("success", "database", f"Database initialized (v{CURRENT_SCHEMA_VERSION})", {
                    "path": self.db_path,
                    "tables": len(CORE_TABLES),
                    "indexes": len(CORE_INDEXES),
                })
            elif current_version < CURRENT_SCHEMA_VERSION:
                # Run migrations
                self._run_migrations(conn, current_version)
            else:
                self.log("success", "database", f"Database verified (v{current_version})", {
                    "path": self.db_path,
                })

            # Verify all tables exist
            self._verify_tables(conn)

    def _run_migrations(self, conn, from_version: int):
        """Run sequential migrations from from_version to CURRENT_SCHEMA_VERSION."""
        cursor = conn.cursor()
        for version in range(from_version + 1, CURRENT_SCHEMA_VERSION + 1):
            if version in MIGRATIONS:
                self.log("info", "database", f"Running migration v{version - 1} → v{version}")
                for sql in MIGRATIONS[version]:
                    try:
                        cursor.execute(sql)
                    except sqlite3.OperationalError as e:
                        # Skip "already exists" errors for idempotent migrations
                        if "already exists" in str(e) or "duplicate column" in str(e):
                            continue
                        raise

        cursor.execute(f"PRAGMA user_version = {CURRENT_SCHEMA_VERSION}")
        conn.commit()
        self.log("success", "database",
                 f"Migrated database from v{from_version} → v{CURRENT_SCHEMA_VERSION}")

    def _verify_tables(self, conn):
        """Verify all core tables exist and log the results."""
        cursor = conn.cursor()
        cursor.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
        existing = {row[0] for row in cursor.fetchall()}

        # Expected table names extracted from CREATE TABLE statements
        expected = set()
        for sql in CORE_TABLES:
            # Extract table name from "CREATE TABLE IF NOT EXISTS table_name ("
            parts = sql.split("(")[0].strip().split()
            for i, part in enumerate(parts):
                if part.upper() == "EXISTS":
                    if i + 1 < len(parts):
                        expected.add(parts[i + 1])
                    break

        missing = expected - existing
        if missing:
            self.log("error", "database", f"Missing tables: {', '.join(sorted(missing))}")
        else:
            self.log("success", "database",
                     f"All {len(expected)} core tables verified [OK]")

    # -----------------------------------------------------------------------
    # Schema Extension API (for mod-registered tables)
    # -----------------------------------------------------------------------
    def register_mod_schema(self, mod_id: str, version: int, tables: list[str] = None,
                            migrations: list[str] = None) -> dict:
        """Register or update mod-specific tables."""
        with self._get_conn() as conn:
            cursor = conn.cursor()

            # Check current mod version
            row = cursor.execute(
                "SELECT version FROM schema_registry WHERE mod_id = ?", (mod_id,)
            ).fetchone()
            current_version = row[0] if row else 0

            if current_version >= version:
                return {"status": "current", "version": current_version}

            # Run table creation
            if tables:
                for sql in tables:
                    try:
                        cursor.execute(sql)
                    except sqlite3.OperationalError as e:
                        if "already exists" not in str(e):
                            return {"status": "error", "error": str(e)}

            # Run migrations
            if migrations and current_version > 0:
                for sql in migrations:
                    try:
                        cursor.execute(sql)
                    except sqlite3.OperationalError as e:
                        if "already exists" not in str(e) and "duplicate column" not in str(e):
                            return {"status": "error", "error": str(e)}

            # Extract table names from SQL
            table_names = []
            if tables:
                for sql in tables:
                    parts = sql.split("(")[0].strip().split()
                    for i, part in enumerate(parts):
                        if part.upper() == "EXISTS" and i + 1 < len(parts):
                            table_names.append(parts[i + 1])
                            break
                        elif part.upper() == "TABLE" and i + 1 < len(parts):
                            name = parts[i + 1]
                            if name.upper() not in ("IF",):
                                table_names.append(name)
                                break

            # Upsert schema_registry
            now = datetime.now(timezone.utc).isoformat()
            cursor.execute("""
                INSERT INTO schema_registry (mod_id, version, table_names, registered_at, updated_at)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(mod_id) DO UPDATE SET
                    version = excluded.version,
                    table_names = excluded.table_names,
                    updated_at = excluded.updated_at
            """, (mod_id, version, json.dumps(table_names), now, now))

            conn.commit()
            self.log("success", "database",
                     f"Registered mod schema: {mod_id} v{version} ({len(table_names)} tables)")
            return {"status": "registered", "version": version, "tables": table_names}

    # -----------------------------------------------------------------------
    # Colony CRUD
    # -----------------------------------------------------------------------
    def register_colony(self, data: dict) -> dict:
        """Register or update a colony."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()

            # Check if colony exists by name
            existing = cursor.execute(
                "SELECT id FROM colonies WHERE colony_name = ?", (data.get("colony_name"),)
            ).fetchone()

            if existing:
                colony_id = existing[0]
                cursor.execute("""
                    UPDATE colonies SET
                        faction_name = COALESCE(?, faction_name),
                        biome = COALESCE(?, biome),
                        seed = COALESCE(?, seed),
                        tile_id = COALESCE(?, tile_id),
                        scenario = COALESCE(?, scenario),
                        ideology_name = COALESCE(?, ideology_name),
                        year_started = COALESCE(?, year_started),
                        last_played_at = ?
                    WHERE id = ?
                """, (
                    data.get("faction_name"), data.get("biome"), data.get("seed"),
                    data.get("tile_id"), data.get("scenario"), data.get("ideology_name"),
                    data.get("year_started"), now, colony_id,
                ))
            else:
                cursor.execute("""
                    INSERT INTO colonies (colony_name, faction_name, biome, seed, tile_id,
                                         scenario, ideology_name, year_started, created_at, last_played_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    data.get("colony_name"), data.get("faction_name"), data.get("biome"),
                    data.get("seed"), data.get("tile_id"), data.get("scenario"),
                    data.get("ideology_name"), data.get("year_started"), now, now,
                ))
                colony_id = cursor.lastrowid

            conn.commit()
            return {"colony_id": colony_id, "status": "updated" if existing else "created"}

    def get_colony(self, colony_id: int) -> dict | None:
        """Get colony info by ID."""
        with self._get_conn() as conn:
            row = conn.execute("SELECT * FROM colonies WHERE id = ?", (colony_id,)).fetchone()
            return dict(row) if row else None

    # -----------------------------------------------------------------------
    # Pawn CRUD
    # -----------------------------------------------------------------------
    def register_pawn(self, data: dict) -> dict:
        """Register or update a pawn with optional traits and skills."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()
            colony_id = data.get("colony_id")
            pawn_id_game = data.get("pawn_id_game")

            # Upsert pawn
            existing = cursor.execute(
                "SELECT id FROM pawns WHERE colony_id = ? AND pawn_id_game = ?",
                (colony_id, pawn_id_game)
            ).fetchone()

            pawn_fields = (
                colony_id, pawn_id_game,
                data.get("name_first"), data.get("name_nick"), data.get("name_last"),
                data.get("gender"), data.get("age_biological"), data.get("age_chronological"),
                data.get("backstory_childhood"), data.get("backstory_childhood_desc"),
                data.get("backstory_adulthood"), data.get("backstory_adulthood_desc"),
                data.get("faction"), data.get("title"), data.get("ideology"),
                data.get("xenotype"), data.get("pawn_kind"),
                data.get("is_alive", True),
            )

            if existing:
                pawn_id = existing[0]
                cursor.execute("""
                    UPDATE pawns SET
                        name_first=?, name_nick=?, name_last=?,
                        gender=?, age_biological=?, age_chronological=?,
                        backstory_childhood=?, backstory_childhood_desc=?,
                        backstory_adulthood=?, backstory_adulthood_desc=?,
                        faction=?, title=?, ideology=?, xenotype=?, pawn_kind=?,
                        is_alive=?, updated_at=?
                    WHERE id = ?
                """, (
                    *pawn_fields[2:], now, pawn_id,
                ))
            else:
                cursor.execute("""
                    INSERT INTO pawns (colony_id, pawn_id_game, name_first, name_nick, name_last,
                                       gender, age_biological, age_chronological,
                                       backstory_childhood, backstory_childhood_desc,
                                       backstory_adulthood, backstory_adulthood_desc,
                                       faction, title, ideology, xenotype, pawn_kind,
                                       is_alive, created_at, updated_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (*pawn_fields, now, now))
                pawn_id = cursor.lastrowid

            # Update traits if provided
            traits = data.get("traits")
            if traits is not None:
                cursor.execute("DELETE FROM pawn_traits WHERE pawn_id = ?", (pawn_id,))
                for t in traits:
                    cursor.execute("""
                        INSERT INTO pawn_traits (pawn_id, trait_def, label, degree, description)
                        VALUES (?, ?, ?, ?, ?)
                    """, (pawn_id, t.get("trait_def", t.get("label", "")),
                          t.get("label", ""), t.get("degree", 0), t.get("description")))

            # Update skills if provided
            skills = data.get("skills")
            if skills is not None:
                for s in skills:
                    cursor.execute("""
                        INSERT INTO pawn_skills (pawn_id, skill_name, level, passion)
                        VALUES (?, ?, ?, ?)
                        ON CONFLICT(pawn_id, skill_name) DO UPDATE SET
                            level = excluded.level,
                            passion = excluded.passion
                    """, (pawn_id, s.get("skill_name"), s.get("level", 0),
                          s.get("passion", "None")))

            conn.commit()
            return {"pawn_id": pawn_id, "status": "updated" if existing else "created"}

    def get_pawn(self, pawn_id: int) -> dict | None:
        """Get full pawn identity including traits and skills."""
        with self._get_conn() as conn:
            row = conn.execute("SELECT * FROM pawns WHERE id = ?", (pawn_id,)).fetchone()
            if not row:
                return None

            pawn = dict(row)
            pawn["traits"] = [dict(r) for r in conn.execute(
                "SELECT trait_def, label, degree, description FROM pawn_traits WHERE pawn_id = ?",
                (pawn_id,)
            ).fetchall()]
            pawn["skills"] = [dict(r) for r in conn.execute(
                "SELECT skill_name, level, passion FROM pawn_skills WHERE pawn_id = ?",
                (pawn_id,)
            ).fetchall()]
            return pawn

    def get_pawn_by_game_id(self, colony_id: int, pawn_id_game: str) -> dict | None:
        """Get pawn by game's internal ID."""
        with self._get_conn() as conn:
            row = conn.execute(
                "SELECT id FROM pawns WHERE colony_id = ? AND pawn_id_game = ?",
                (colony_id, pawn_id_game)
            ).fetchone()
            if row:
                return self.get_pawn(row[0])
            return None

    def list_pawns(self, colony_id: int) -> list[dict]:
        """List all pawns in a colony (identity only, no traits/skills for performance)."""
        with self._get_conn() as conn:
            rows = conn.execute(
                "SELECT * FROM pawns WHERE colony_id = ? ORDER BY name_nick",
                (colony_id,)
            ).fetchall()
            return [dict(r) for r in rows]

    # -----------------------------------------------------------------------
    # Memory CRUD
    # -----------------------------------------------------------------------
    def store_memory(self, data: dict) -> dict:
        """Store a new memory/event."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()

            cursor.execute("""
                INSERT INTO memories (colony_id, pawn_id, memory_type, summary,
                                     participants, tags, game_tick, in_game_date,
                                     weight, base_weight, decay_rate,
                                     created_at, last_decayed_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                data.get("colony_id"), data.get("pawn_id"), data.get("memory_type"),
                data.get("summary"),
                json.dumps(data.get("participants")) if data.get("participants") else None,
                json.dumps(data.get("tags")) if data.get("tags") else None,
                data.get("game_tick"), data.get("in_game_date"),
                data.get("weight", data.get("base_weight", 0.8)),
                data.get("base_weight", 0.8),
                data.get("decay_rate", 0.05),
                now, now,
            ))
            conn.commit()
            return {"memory_id": cursor.lastrowid, "status": "stored"}

    def query_memories(self, colony_id: int, pawn_id: int = None, memory_type: str = None,
                       weight_threshold: float = 0.05, limit: int = 10, tags: list = None) -> list[dict]:
        """Query memories by relevance (weight descending)."""
        with self._get_conn() as conn:
            sql = "SELECT * FROM memories WHERE colony_id = ? AND weight > ?"
            params = [colony_id, weight_threshold]

            if pawn_id is not None:
                sql += " AND (pawn_id = ? OR pawn_id IS NULL)"
                params.append(pawn_id)

            if memory_type:
                sql += " AND memory_type = ?"
                params.append(memory_type)

            if tags:
                for tag in tags:
                    sql += " AND tags LIKE ?"
                    params.append(f"%{tag}%")

            sql += " ORDER BY weight DESC LIMIT ?"
            params.append(limit)

            rows = conn.execute(sql, params).fetchall()
            return [dict(r) for r in rows]

    def bump_memory(self, memory_id: int, bump_amount: float = 0.2) -> dict:
        """Bump a memory's weight (it was referenced by the LLM)."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            conn.execute("""
                UPDATE memories SET
                    weight = MIN(1.0, weight + ?),
                    times_referenced = times_referenced + 1,
                    last_referenced_at = ?
                WHERE id = ?
            """, (bump_amount, now, memory_id))
            conn.commit()
            return {"status": "bumped"}

    def decay_memories(self, colony_id: int) -> dict:
        """Run a decay cycle on all memories in a colony."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            cursor = conn.execute("""
                UPDATE memories SET
                    weight = MAX(0.0, weight - decay_rate),
                    last_decayed_at = ?
                WHERE colony_id = ? AND weight > 0.0
            """, (now, colony_id))
            conn.commit()
            return {"status": "decayed", "affected": cursor.rowcount}

    def delete_memory(self, memory_id: int) -> dict:
        """Delete a memory by ID."""
        with self._get_conn() as conn:
            conn.execute("DELETE FROM memories WHERE id = ?", (memory_id,))
            conn.commit()
            return {"status": "deleted"}

    # -----------------------------------------------------------------------
    # Relationship CRUD
    # -----------------------------------------------------------------------
    def update_relationship(self, data: dict) -> dict:
        """Create or update a pawn-to-pawn relationship."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()

            existing = cursor.execute(
                "SELECT id FROM relationships WHERE colony_id = ? AND pawn_a_id = ? AND pawn_b_id = ?",
                (data["colony_id"], data["pawn_a_id"], data["pawn_b_id"])
            ).fetchone()

            if existing:
                rel_id = existing[0]
                cursor.execute("""
                    UPDATE relationships SET
                        relation_type = COALESCE(?, relation_type),
                        opinion_a_to_b = COALESCE(?, opinion_a_to_b),
                        opinion_b_to_a = COALESCE(?, opinion_b_to_a),
                        arc_phase = COALESCE(?, arc_phase),
                        interaction_count = interaction_count + 1,
                        updated_at = ?
                    WHERE id = ?
                """, (
                    data.get("relation_type"), data.get("opinion_a_to_b"),
                    data.get("opinion_b_to_a"), data.get("arc_phase"),
                    now, rel_id,
                ))
            else:
                cursor.execute("""
                    INSERT INTO relationships (colony_id, pawn_a_id, pawn_b_id, relation_type,
                                              opinion_a_to_b, opinion_b_to_a, updated_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                """, (
                    data["colony_id"], data["pawn_a_id"], data["pawn_b_id"],
                    data.get("relation_type"), data.get("opinion_a_to_b", 0),
                    data.get("opinion_b_to_a", 0), now,
                ))
                rel_id = cursor.lastrowid

            conn.commit()
            return {"relationship_id": rel_id, "status": "updated" if existing else "created"}

    def query_relationships(self, pawn_id: int, weight_threshold: float = 0.0) -> list[dict]:
        """Get all relationships for a pawn, ordered by weight."""
        with self._get_conn() as conn:
            rows = conn.execute("""
                SELECT r.*, 
                       pa.name_nick as pawn_a_name,
                       pb.name_nick as pawn_b_name
                FROM relationships r
                JOIN pawns pa ON r.pawn_a_id = pa.id
                JOIN pawns pb ON r.pawn_b_id = pb.id
                WHERE (r.pawn_a_id = ? OR r.pawn_b_id = ?) AND r.weight > ?
                ORDER BY r.weight DESC
            """, (pawn_id, pawn_id, weight_threshold)).fetchall()
            return [dict(r) for r in rows]

    def record_opinion_sample(self, relationship_id: int, opinion_a_to_b: int,
                              opinion_b_to_a: int, game_tick: int = None) -> dict:
        """Record an opinion sample and update the integral (moving average)."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()

            # Insert sample
            cursor.execute("""
                INSERT INTO opinion_samples (relationship_id, opinion_a_to_b, opinion_b_to_a,
                                             game_tick, sampled_at)
                VALUES (?, ?, ?, ?, ?)
            """, (relationship_id, opinion_a_to_b, opinion_b_to_a, game_tick, now))

            # Update integral via incremental moving average
            cursor.execute("""
                UPDATE relationships SET
                    opinion_a_to_b = ?,
                    opinion_b_to_a = ?,
                    integral_a_to_b = integral_a_to_b + (? - integral_a_to_b) / (integral_samples + 1),
                    integral_b_to_a = integral_b_to_a + (? - integral_b_to_a) / (integral_samples + 1),
                    integral_samples = integral_samples + 1,
                    peak_high_a_to_b = MAX(peak_high_a_to_b, ?),
                    peak_low_a_to_b = MIN(peak_low_a_to_b, ?),
                    peak_high_b_to_a = MAX(peak_high_b_to_a, ?),
                    peak_low_b_to_a = MIN(peak_low_b_to_a, ?),
                    updated_at = ?
                WHERE id = ?
            """, (
                opinion_a_to_b, opinion_b_to_a,
                opinion_a_to_b, opinion_b_to_a,
                opinion_a_to_b, opinion_a_to_b,
                opinion_b_to_a, opinion_b_to_a,
                now, relationship_id,
            ))

            conn.commit()
            return {"status": "sampled"}

    # -----------------------------------------------------------------------
    # Interaction & Thread CRUD
    # -----------------------------------------------------------------------
    def store_interaction(self, data: dict) -> dict:
        """Record a completed interaction/conversation."""
        with self._get_conn() as conn:
            cursor = conn.cursor()
            now = datetime.now(timezone.utc).isoformat()

            cursor.execute("""
                INSERT INTO interactions (colony_id, memory_id, player_pawn_id, npc_pawn_id,
                                          npc_name, npc_faction, interaction_type,
                                          situation_summary, outcome, outcome_details,
                                          opinion_delta, relationship_delta,
                                          extracted_keywords, narrative_summary,
                                          game_tick, started_at, ended_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                data.get("colony_id"), data.get("memory_id"),
                data.get("player_pawn_id"), data.get("npc_pawn_id"),
                data.get("npc_name"), data.get("npc_faction"),
                data.get("interaction_type"),
                data.get("situation_summary"), data.get("outcome"),
                json.dumps(data.get("outcome_details")) if data.get("outcome_details") else None,
                data.get("opinion_delta", 0), data.get("relationship_delta", 0),
                json.dumps(data.get("extracted_keywords")) if data.get("extracted_keywords") else None,
                data.get("narrative_summary"),
                data.get("game_tick"), now, data.get("ended_at"),
            ))
            conn.commit()
            return {"interaction_id": cursor.lastrowid, "status": "stored"}

    def add_interaction_message(self, interaction_id: int, turn_number: int,
                                role: str, content: str, sentiment: float = None,
                                mood_shift: int = 0) -> dict:
        """Append a message to an interaction."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            conn.execute("""
                INSERT INTO interaction_messages (interaction_id, turn_number, role,
                                                  content, sentiment, mood_shift, created_at)
                VALUES (?, ?, ?, ?, ?, ?, ?)
            """, (interaction_id, turn_number, role, content, sentiment, mood_shift, now))
            conn.commit()
            return {"status": "added"}

    def get_interaction(self, interaction_id: int) -> dict | None:
        """Get a full interaction with all messages."""
        with self._get_conn() as conn:
            row = conn.execute(
                "SELECT * FROM interactions WHERE id = ?", (interaction_id,)
            ).fetchone()
            if not row:
                return None

            interaction = dict(row)
            interaction["messages"] = [dict(r) for r in conn.execute(
                "SELECT * FROM interaction_messages WHERE interaction_id = ? ORDER BY turn_number",
                (interaction_id,)
            ).fetchall()]
            return interaction

    def store_thread(self, data: dict) -> dict:
        """Create a narrative thread."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO narrative_threads (colony_id, keyword, category, description,
                                               source_interaction_id, source_memory_id,
                                               weight, decay_rate, created_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                data.get("colony_id"), data.get("keyword"), data.get("category"),
                data.get("description"),
                data.get("source_interaction_id"), data.get("source_memory_id"),
                data.get("weight", 0.6), data.get("decay_rate", 0.03), now,
            ))
            conn.commit()
            return {"thread_id": cursor.lastrowid, "status": "created"}

    def get_active_threads(self, colony_id: int, weight_threshold: float = 0.05) -> list[dict]:
        """Get active (unresolved, weighted) narrative threads."""
        with self._get_conn() as conn:
            rows = conn.execute("""
                SELECT * FROM narrative_threads
                WHERE colony_id = ? AND is_resolved = 0 AND weight > ?
                ORDER BY weight DESC
            """, (colony_id, weight_threshold)).fetchall()
            return [dict(r) for r in rows]

    def bump_thread(self, thread_id: int, bump_amount: float = 0.15) -> dict:
        """Bump a thread's weight (it was referenced)."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            conn.execute("""
                UPDATE narrative_threads SET
                    weight = MIN(1.0, weight + ?),
                    times_referenced = times_referenced + 1,
                    last_referenced_at = ?
                WHERE id = ?
            """, (bump_amount, now, thread_id))
            conn.commit()
            return {"status": "bumped"}

    def resolve_thread(self, thread_id: int, resolution_summary: str = None) -> dict:
        """Mark a narrative thread as resolved."""
        with self._get_conn() as conn:
            conn.execute("""
                UPDATE narrative_threads SET
                    is_resolved = 1,
                    resolution_summary = ?
                WHERE id = ?
            """, (resolution_summary, thread_id))
            conn.commit()
            return {"status": "resolved"}

    # -----------------------------------------------------------------------
    # Context Assembly
    # -----------------------------------------------------------------------
    def build_context(self, data: dict) -> dict:
        """
        Assemble context from the database based on a generic object reference.

        The mod sends event_type, source/target pawn IDs, and framing.
        The bridge queries the DB, filters by settings, and returns
        structured data + a ready-to-use prompt.
        """
        colony_id = data.get("colony_id")
        settings = data.get("settings", {})
        memory_limit = settings.get("memory_limit", 5)
        weight_threshold = settings.get("weight_threshold", 0.1)

        result = {"data": {}, "prompt": "", "tokens_estimated": 0,
                  "memories_included": 0, "threads_included": 0}

        # Colony
        colony = self.get_colony(colony_id)
        if colony:
            result["data"]["colony"] = {
                "name": colony["colony_name"],
                "biome": colony.get("biome"),
                "scenario": colony.get("scenario"),
                "ideology": colony.get("ideology_name"),
            }

        # Source pawn
        source_pawn_ref = data.get("source_pawn")
        source_pawn = None
        if source_pawn_ref:
            source_pawn = self.get_pawn_by_game_id(colony_id, source_pawn_ref)
            if not source_pawn:
                # Try as integer ID
                try:
                    source_pawn = self.get_pawn(int(source_pawn_ref))
                except (ValueError, TypeError):
                    pass

        if source_pawn:
            pawn_data = self._build_pawn_context(source_pawn, colony_id,
                                                 memory_limit, weight_threshold, settings)
            result["data"]["source_pawn"] = pawn_data
            result["memories_included"] += len(pawn_data.get("top_memories", []))

        # Target pawn
        target_pawn_ref = data.get("target_pawn")
        target_pawn = None
        if target_pawn_ref:
            target_pawn = self.get_pawn_by_game_id(colony_id, target_pawn_ref)
            if not target_pawn:
                try:
                    target_pawn = self.get_pawn(int(target_pawn_ref))
                except (ValueError, TypeError):
                    pass

        if target_pawn:
            pawn_data = self._build_pawn_context(target_pawn, colony_id,
                                                 memory_limit, weight_threshold, settings)
            result["data"]["target_pawn"] = pawn_data
            result["memories_included"] += len(pawn_data.get("top_memories", []))

        # Active narrative threads
        if settings.get("include_threads", True):
            threads = self.get_active_threads(colony_id, weight_threshold)
            result["data"]["active_threads"] = [
                {"keyword": t["keyword"], "category": t.get("category"),
                 "description": t["description"], "weight": t["weight"]}
                for t in threads
            ]
            result["threads_included"] = len(threads)

        # Build prompt
        framing = data.get("framing", "")
        event_type = data.get("event_type", "general")
        result["prompt"] = self._build_prompt(event_type, framing, result["data"])
        result["tokens_estimated"] = len(result["prompt"]) // 4

        return result

    def _build_pawn_context(self, pawn: dict, colony_id: int,
                            memory_limit: int, weight_threshold: float,
                            settings: dict) -> dict:
        """Build context data for a single pawn based on settings."""
        ctx = {"name": pawn.get("name_nick") or pawn.get("name_first", "Unknown")}

        if settings.get("include_traits", True) and pawn.get("traits"):
            ctx["traits"] = [t["label"] for t in pawn["traits"]]

        if settings.get("include_backstory", True):
            backstory_parts = []
            if pawn.get("backstory_childhood"):
                backstory_parts.append(pawn["backstory_childhood"])
            if pawn.get("backstory_adulthood"):
                backstory_parts.append(pawn["backstory_adulthood"])
            if backstory_parts:
                ctx["backstory"] = " -> ".join(backstory_parts)

        if settings.get("include_memories", True):
            memories = self.query_memories(
                colony_id, pawn_id=pawn["id"],
                weight_threshold=weight_threshold, limit=memory_limit
            )
            ctx["top_memories"] = [
                {"summary": m["summary"], "weight": round(m["weight"], 2),
                 "type": m["memory_type"]}
                for m in memories
            ]

        if settings.get("include_relationships", True):
            rels = self.query_relationships(pawn["id"], weight_threshold)
            ctx["relationships"] = []
            for r in rels:
                other_name = r["pawn_b_name"] if r["pawn_a_id"] == pawn["id"] else r["pawn_a_name"]
                opinion = r["opinion_a_to_b"] if r["pawn_a_id"] == pawn["id"] else r["opinion_b_to_a"]
                integral = r["integral_a_to_b"] if r["pawn_a_id"] == pawn["id"] else r["integral_b_to_a"]
                ctx["relationships"].append({
                    "with": other_name,
                    "type": r.get("relation_type"),
                    "opinion": opinion,
                    "integral": round(integral, 1),
                })

        return ctx

    def _build_prompt(self, event_type: str, framing: str, data: dict) -> str:
        """Build a prompt string from assembled context data."""
        parts = []

        # Source pawn identity
        source = data.get("source_pawn")
        if source:
            name = source.get("name", "Unknown")
            traits = ", ".join(source.get("traits", []))
            parts.append(f"You are {name}" + (f", a {traits} colonist" if traits else "") + ".")

            backstory = source.get("backstory")
            if backstory:
                parts.append(f"Background: {backstory}.")

        # Framing / situation
        if framing:
            parts.append(f"\nSituation: {framing}")

        # Target pawn
        target = data.get("target_pawn")
        if target:
            target_name = target.get("name", "Unknown")
            target_traits = ", ".join(target.get("traits", []))
            parts.append(f"\n{target_name}" + (f" ({target_traits})" if target_traits else "") + ":")

            # Relationship between source and target
            if source and source.get("relationships"):
                for rel in source["relationships"]:
                    if rel.get("with") == target_name:
                        parts.append(
                            f"Your opinion of {target_name}: {rel['opinion']} "
                            f"(deep sentiment: {rel['integral']})"
                            + (f", {rel['type']}" if rel.get("type") else "")
                        )
                        break

        # Memories
        if source and source.get("top_memories"):
            parts.append("\nRecent memories:")
            for m in source["top_memories"]:
                parts.append(f"- {m['summary']}")

        if target and target.get("top_memories"):
            target_name = target.get("name", "Unknown")
            parts.append(f"\n{target_name}'s recent memories:")
            for m in target["top_memories"]:
                parts.append(f"- {m['summary']}")

        # Narrative threads
        threads = data.get("active_threads", [])
        if threads:
            parts.append("\nActive rumors/events in the world:")
            for t in threads:
                parts.append(f"- {t['description']}")

        # Colony
        colony = data.get("colony")
        if colony:
            colony_info = f"\nColony: {colony.get('name', 'Unknown')}"
            if colony.get("biome"):
                colony_info += f" ({colony['biome']})"
            parts.append(colony_info)

        # Instruction based on event type
        instructions = {
            "relationship": "Respond in character. Show how this relationship dynamic plays out.",
            "dialogue": "Respond in character as this colonist.",
            "reaction": "Describe this colonist's reaction to the situation.",
            "event": "Generate a narrative event that fits this colony's current state.",
            "quest": "Describe this quest-related interaction.",
        }
        instruction = instructions.get(event_type, "Respond in character.")
        parts.append(f"\n{instruction}")

        return "\n".join(parts)

    # -----------------------------------------------------------------------
    # Prompt Log
    # -----------------------------------------------------------------------
    def log_prompt(self, data: dict) -> dict:
        """Log a prompt/response for audit and tuning."""
        with self._get_conn() as conn:
            now = datetime.now(timezone.utc).isoformat()
            cursor = conn.cursor()
            cursor.execute("""
                INSERT INTO prompt_log (colony_id, request_type, pawn_id,
                                        prompt_text, response_text, memory_ids_used,
                                        tokens_used, duration_ms, game_tick, created_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """, (
                data.get("colony_id"), data.get("request_type"), data.get("pawn_id"),
                data.get("prompt_text"), data.get("response_text"),
                json.dumps(data.get("memory_ids_used")) if data.get("memory_ids_used") else None,
                data.get("tokens_used"), data.get("duration_ms"),
                data.get("game_tick"), now,
            ))
            conn.commit()
            return {"log_id": cursor.lastrowid, "status": "logged"}

    # -----------------------------------------------------------------------
    # Dashboard Stats
    # -----------------------------------------------------------------------
    def get_stats(self) -> dict:
        """Get comprehensive database statistics for the dashboard."""
        with self._get_conn() as conn:
            cursor = conn.cursor()

            # Table row counts
            tables = {}
            for table in ["colonies", "pawns", "pawn_traits", "pawn_skills",
                          "memories", "relationships", "opinion_samples",
                          "interactions", "interaction_messages", "narrative_threads",
                          "prompt_log", "schema_registry"]:
                try:
                    count = cursor.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
                    tables[table] = count
                except Exception:
                    tables[table] = 0

            # Schema version
            schema_version = cursor.execute("PRAGMA user_version").fetchone()[0]

            # DB file size
            try:
                db_size_bytes = os.path.getsize(self.db_path)
            except Exception:
                db_size_bytes = 0

            # Colony summaries
            colonies = []
            for row in cursor.execute("""
                SELECT c.id, c.colony_name, c.biome, c.ideology_name,
                       (SELECT COUNT(*) FROM pawns WHERE colony_id = c.id AND is_alive = 1) as pawn_count,
                       (SELECT COUNT(*) FROM memories WHERE colony_id = c.id) as memory_count,
                       (SELECT COUNT(*) FROM relationships WHERE colony_id = c.id) as relationship_count,
                       (SELECT COUNT(*) FROM narrative_threads WHERE colony_id = c.id AND is_resolved = 0) as active_threads
                FROM colonies c ORDER BY c.last_played_at DESC
            """).fetchall():
                colonies.append(dict(row))

            # Mod registry
            mods = []
            for row in cursor.execute("SELECT * FROM schema_registry ORDER BY updated_at DESC").fetchall():
                mods.append(dict(row))

            # Memory weight distribution (for heatmap)
            weight_dist = {"decaying": 0, "stable": 0, "active": 0, "hot": 0}
            for row in cursor.execute("""
                SELECT
                    SUM(CASE WHEN weight < 0.2 THEN 1 ELSE 0 END) as decaying,
                    SUM(CASE WHEN weight >= 0.2 AND weight < 0.5 THEN 1 ELSE 0 END) as stable,
                    SUM(CASE WHEN weight >= 0.5 AND weight < 0.8 THEN 1 ELSE 0 END) as active,
                    SUM(CASE WHEN weight >= 0.8 THEN 1 ELSE 0 END) as hot
                FROM memories
            """).fetchall():
                weight_dist = {
                    "decaying": row[0] or 0,
                    "stable": row[1] or 0,
                    "active": row[2] or 0,
                    "hot": row[3] or 0,
                }

            # Thread weight distribution
            thread_dist = {"fading": 0, "active": 0, "hot": 0}
            for row in cursor.execute("""
                SELECT
                    SUM(CASE WHEN weight < 0.3 THEN 1 ELSE 0 END) as fading,
                    SUM(CASE WHEN weight >= 0.3 AND weight < 0.7 THEN 1 ELSE 0 END) as active,
                    SUM(CASE WHEN weight >= 0.7 THEN 1 ELSE 0 END) as hot
                FROM narrative_threads WHERE is_resolved = 0
            """).fetchall():
                thread_dist = {
                    "fading": row[0] or 0,
                    "active": row[1] or 0,
                    "hot": row[2] or 0,
                }

            return {
                "schema_version": schema_version,
                "db_size_bytes": db_size_bytes,
                "tables": tables,
                "colonies": colonies,
                "mods": mods,
                "weight_distribution": weight_dist,
                "thread_distribution": thread_dist,
                "totals": {
                    "colonies": tables.get("colonies", 0),
                    "pawns": tables.get("pawns", 0),
                    "memories": tables.get("memories", 0),
                    "relationships": tables.get("relationships", 0),
                    "interactions": tables.get("interactions", 0),
                    "threads": tables.get("narrative_threads", 0),
                },
            }

    def get_recent_memories(self, limit: int = 20) -> list[dict]:
        """Get most recent memories across all colonies for the live feed."""
        with self._get_conn() as conn:
            rows = conn.execute("""
                SELECT m.*, p.name_nick as pawn_name, c.colony_name
                FROM memories m
                LEFT JOIN pawns p ON m.pawn_id = p.id
                LEFT JOIN colonies c ON m.colony_id = c.id
                ORDER BY m.created_at DESC
                LIMIT ?
            """, (limit,)).fetchall()
            return [dict(r) for r in rows]

    def get_pawn_details_for_dashboard(self, colony_id: int) -> list[dict]:
        """Get pawns with traits and key relationship info for the dashboard."""
        with self._get_conn() as conn:
            pawns = []
            for row in conn.execute("""
                SELECT p.*, 
                       (SELECT COUNT(*) FROM relationships 
                        WHERE pawn_a_id = p.id OR pawn_b_id = p.id) as relationship_count,
                       (SELECT COUNT(*) FROM memories WHERE pawn_id = p.id) as memory_count
                FROM pawns p
                WHERE p.colony_id = ? AND p.is_alive = 1
                ORDER BY p.name_nick
            """, (colony_id,)).fetchall():
                pawn = dict(row)
                pawn["traits"] = [dict(r) for r in conn.execute(
                    "SELECT label, degree FROM pawn_traits WHERE pawn_id = ?", (pawn["id"],)
                ).fetchall()]
                pawns.append(pawn)
            return pawns


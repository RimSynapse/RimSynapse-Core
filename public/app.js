// UI Elements
const elBridgePort = document.getElementById('lbl-bridge-port');
const elLmStatusPill = document.getElementById('pill-lm-status');
const elLmStatusText = document.getElementById('lbl-lm-status');
const elQueueSize = document.getElementById('lbl-queue-size');
const elQueueProcessor = document.getElementById('lbl-queue-processor');
const elQueueDot = document.querySelector('.queue-dot');
const elProgressQueue = document.getElementById('progress-queue');
const elModelsList = document.getElementById('models-list');
const elLogsContainer = document.getElementById('logs-container');
const elBtnClearLogs = document.getElementById('btn-clear-logs');
const elBtnRefreshModels = document.getElementById('btn-refresh-models');
const elChkShowSysLogs = document.getElementById('chk-show-sys-logs');

// GPU VRAM DOM elements
const elGpuMonitorContainer = document.getElementById('gpu-card-sidebar');
const elGpuTotalUsed = document.getElementById('gpu-total-used');
const elGpuTotalMax = document.getElementById('gpu-total-max');
const elGpuUtilPct = document.getElementById('gpu-util-pct');
const elGpuTotalBar = document.getElementById('gpu-total-bar');
const elGpuModelUsed = document.getElementById('gpu-model-used');
const elGpuModelDesc = document.getElementById('gpu-model-desc');
const elGpuModelPct = document.getElementById('gpu-model-pct');
const elGpuGameUsed = document.getElementById('gpu-game-used');
const elGpuGameDesc = document.getElementById('gpu-game-desc');
const elGpuGamePct = document.getElementById('gpu-game-pct');
const elGpuOtherUsed = document.getElementById('gpu-other-used');
const elGpuOtherPct = document.getElementById('gpu-other-pct');
const elGpuOtherDesc = document.getElementById('gpu-other-desc');

// RimWorld Ticks & Concurrency Scalability Planner DOM elements
const elCapacityPawnCount = document.getElementById('capacity-pawn-count');
const elCapacityPawnList = document.getElementById('capacity-pawn-list');
const elCapacityAvgDurationSecs = document.getElementById('capacity-avg-duration-secs');
const elCapacityAvgPromptSize = document.getElementById('capacity-avg-prompt-size');
const elCapacityFrequencyTps = document.getElementById('capacity-frequency-tps');
const elCapacityQueueState = document.getElementById('capacity-queue-state');

// Sim controls
const elSimPawnCount = document.getElementById('sim-pawn-count');
const elSimTpsSlider = document.getElementById('sim-tps-slider');
const elLblSimTps = document.getElementById('lbl-sim-tps');

// Recommendations outputs
const elXmlPawnTicks = document.getElementById('xml-pawn-ticks');
const elXmlStorytellerTicks = document.getElementById('xml-storyteller-ticks');
const elXmlAdvisorTicks = document.getElementById('xml-advisor-ticks');
const elXmlSummaryTicks = document.getElementById('xml-summary-ticks');

const elConfigForm = document.getElementById('config-form');
const elInputPort = document.getElementById('input-port');
const elInputUrl = document.getElementById('input-url');
const elInputBridgeKey = document.getElementById('input-bridge-key');
const elInputLmKey = document.getElementById('input-lm-key');
const elChkAutoMap = document.getElementById('chk-auto-map');
const elChkSanitize = document.getElementById('chk-sanitize');

// Playground Elements
const elTxtPlaygroundPrompt = document.getElementById('txt-playground-prompt');
const elBtnTestPing = document.getElementById('btn-test-ping');
const elBtnSubmitTest = document.getElementById('btn-submit-test');
const elSpinnerTest = document.getElementById('spinner-test');
const elPlaygroundResultBox = document.getElementById('playground-result-box');
const elPlaygroundOutput = document.getElementById('playground-output');

// Modal Elements
const elModalOverlay = document.getElementById('log-detail-modal');
const elBtnCloseModal = document.getElementById('btn-close-modal');
const elDetailTime = document.getElementById('detail-time');
const elDetailType = document.getElementById('detail-type');
const elDetailDuration = document.getElementById('detail-duration');
const elDetailTokens = document.getElementById('detail-tokens');
const elDetailCode = document.getElementById('detail-code');

// Logs state map to easily retrieve log details on click
const logsMap = new Map();

// Initialize UI Config Form
let initializedConfig = false;
let initializedContext = false;

// 1. Fetch Current Status
async function updateStatus() {
  try {
    const response = await fetch('/api/status');
    if (!response.ok) throw new Error('Failed to fetch status');
    const data = await response.json();
    
    // Update labels
    elBridgePort.textContent = data.config.port;
    
    // Update LM Studio Online Pill
    if (data.lmStudio.online) {
      elLmStatusPill.className = 'status-pill status-lm-online';
      elLmStatusText.textContent = 'ONLINE';
    } else {
      elLmStatusPill.className = 'status-pill status-lm-offline';
      elLmStatusText.textContent = 'OFFLINE';
    }
    
    // Update Queue
    elQueueSize.textContent = data.queueSize;
    if (data.processingQueue) {
      elQueueProcessor.textContent = 'Active';
      elQueueProcessor.className = 'value status-active';
      elProgressQueue.style.width = '100%';
      if (elQueueDot) elQueueDot.classList.add('active');
    } else {
      elQueueProcessor.textContent = 'Idle';
      elQueueProcessor.className = 'value status-inactive';
      elProgressQueue.style.width = data.queueSize > 0 ? '50%' : '0%';
      if (elQueueDot) elQueueDot.classList.remove('active');
    }
    
    // Update Models list
    const activeModelId = data.lmStudio.activeContext ? data.lmStudio.activeContext.modelId : null;
    renderModels(data.lmStudio.models, activeModelId);
    
    // Auto-sync loaded model context window if checkbox is active
    if (elChkLinkModelContext.checked && data.lmStudio.activeContext) {
      const activeCtx = data.lmStudio.activeContext;
      if (activeCtx && activeCtx.contextLength) {
        if (elTokenWindow.value !== activeCtx.contextLength.toString()) {
          elTokenWindow.value = activeCtx.contextLength;
          recalculateAllocations();
        }
      }
    }
    
    // Update GPU & VRAM Monitoring
    if (data.gpu && data.gpu.supported) {
      elGpuMonitorContainer.classList.remove('loading-gpu', 'unsupported-gpu');
      
      const totalCapacity = data.gpu.totalVram || 1;
      
      // Update Total VRAM & Load
      elGpuTotalUsed.textContent = data.gpu.usedVram.toFixed(2);
      elGpuTotalMax.textContent = totalCapacity.toFixed(2);
      elGpuUtilPct.textContent = `(${data.gpu.utilization}% Load)`;
      
      const vramPct = (data.gpu.usedVram / totalCapacity) * 100;
      elGpuTotalBar.style.width = `${Math.min(vramPct, 100).toFixed(0)}%`;
      
      // Update process VRAM values
      let lmStudioVram = 0;
      let lmStudioPid = null;
      let rimworldVram = 0;
      let rimworldPid = null;
      
      if (data.gpu.processes && data.gpu.processes.length > 0) {
        data.gpu.processes.forEach(p => {
          if (p.Name && (p.Name.includes('LM Studio') || p.Name.toLowerCase().includes('llama'))) {
            lmStudioVram += p.VramMB;
            lmStudioPid = p.Pid;
          } else if (p.Name && p.Name.includes('RimWorld')) {
            rimworldVram += p.VramMB;
            rimworldPid = p.Pid;
          }
        });
      }
      
      const lmStudioGB = lmStudioVram / 1024;
      const rimworldGB = rimworldVram / 1024;
      const otherGB = Math.max(0, data.gpu.usedVram - lmStudioGB - rimworldGB);
      
      const lmStudioPct = (lmStudioGB / totalCapacity) * 100;
      const rimworldPct = (rimworldGB / totalCapacity) * 100;
      const otherPct = (otherGB / totalCapacity) * 100;
      
      // Update LM Studio details
      elGpuModelUsed.textContent = lmStudioGB.toFixed(2);
      elGpuModelPct.textContent = `${lmStudioPct.toFixed(1)}%`;
      if (lmStudioVram > 0) {
        elGpuModelDesc.innerHTML = `Active <span class="pid-tag">(PID: ${lmStudioPid})</span>`;
        elGpuModelDesc.className = 'desc text-active-green';
      } else {
        elGpuModelDesc.textContent = 'No active model loaded';
        elGpuModelDesc.className = 'desc';
      }
      
      // Update RimWorld details
      elGpuGameUsed.textContent = rimworldGB.toFixed(2);
      elGpuGamePct.textContent = `${rimworldPct.toFixed(1)}%`;
      if (rimworldVram > 0) {
        elGpuGameDesc.innerHTML = `Running <span class="pid-tag">(PID: ${rimworldPid})</span>`;
        elGpuGameDesc.className = 'desc text-active-purple';
      } else {
        elGpuGameDesc.textContent = 'Game inactive';
        elGpuGameDesc.className = 'desc';
      }
      
      // Update Other/System details
      elGpuOtherUsed.textContent = otherGB.toFixed(2);
      elGpuOtherPct.textContent = `${otherPct.toFixed(1)}%`;
      elGpuOtherDesc.textContent = 'OS & Background Apps';
      elGpuOtherDesc.className = 'desc text-active-gray';
    } else {
      elGpuMonitorContainer.classList.remove('loading-gpu');
      elGpuMonitorContainer.classList.add('unsupported-gpu');
      
      // Set fallbacks for display
      elGpuUtilPct.textContent = '(N/A)';
      elGpuTotalUsed.textContent = '0.00';
      elGpuTotalMax.textContent = '0.00';
      elGpuTotalBar.style.width = '0%';
      
      elGpuModelUsed.textContent = '0.00';
      elGpuModelPct.textContent = '0.0%';
      elGpuModelDesc.textContent = 'NVIDIA System Management Interface (nvidia-smi) not found';
      elGpuModelDesc.className = 'desc';
      
      elGpuGameUsed.textContent = '0.00';
      elGpuGamePct.textContent = '0.0%';
      elGpuGameDesc.textContent = 'Hardware monitoring disabled';
      elGpuGameDesc.className = 'desc';
      
      elGpuOtherUsed.textContent = '0.00';
      elGpuOtherPct.textContent = '0.0%';
      elGpuOtherDesc.textContent = 'Check your GPU configuration';
      elGpuOtherDesc.className = 'desc';
    }

    // Update Model Capacity & Scalability Planner
    if (data.capacity) {
      elCapacityPawnCount.textContent = data.capacity.activePawnCount;
      currentTrackedPawnsCount = data.capacity.activePawnCount;
      
      if (data.capacity.activePawnNames && data.capacity.activePawnNames.length > 0) {
        elCapacityPawnList.textContent = `Active: ${data.capacity.activePawnNames.join(', ')}`;
      } else {
        elCapacityPawnList.textContent = 'No active pawns tracked';
      }
      
      const durationSec = (data.capacity.avgDurationMs / 1000);
      currentAvgDurationMs = data.capacity.avgDurationMs || 0;
      elCapacityAvgDurationSecs.innerHTML = `${durationSec.toFixed(1)} <span class="unit">seconds</span>`;
      elCapacityAvgPromptSize.textContent = `Prompt size: ${data.capacity.avgPromptTokens.toLocaleString()} tokens`;
      
      // Calculate queue concurrency
      elCapacityFrequencyTps.innerHTML = `${data.capacity.callsPerMinute.toFixed(1)} <span class="unit">/ min</span>`;
      
      // Queue state
      const state = data.processingQueue ? 'ACTIVE' : 'IDLE';
      elCapacityQueueState.textContent = `State: ${state} (${data.queueSize} queued)`;
      
      // Sync simulated pawn count only initially
      if (!elSimPawnCount.dataset.userEdited && data.capacity.activePawnCount > 0) {
        elSimPawnCount.value = data.capacity.activePawnCount;
      }
      
      // Run calculations
      recalculateScalabilityRecommendations();
    }

    // Initial config load to populate form inputs
    if (!initializedConfig) {
      elInputPort.value = data.config.port;
      elInputUrl.value = data.config.lmStudioUrl;
      elInputBridgeKey.value = data.config.bridgeApiKey || '';
      elInputLmKey.value = data.config.lmStudioApiKey || '';
      elChkAutoMap.checked = data.config.autoMapModel;
      elChkSanitize.checked = data.config.sanitizeResponse;
      initializedConfig = true;
    }
  } catch (err) {
    console.error('Error fetching bridge status:', err);
  }
}

// Render active models list
function renderModels(models, activeModelId = null) {
  if (!models || models.length === 0) {
    elModelsList.innerHTML = `<div class="empty-state">No models loaded. Load one in LM Studio.</div>`;
    elSelectXmlModel.innerHTML = `<option value="auto">Auto-detect from LM Studio</option>`;
    return;
  }
  
  elModelsList.innerHTML = models.map(model => {
    const isActive = activeModelId === model;
    return `
      <div class="model-item ${isActive ? 'active' : ''}" title="${model}">
        <span class="model-badge ${isActive ? 'badge-active' : ''}"></span>
        <span class="model-name">${model}</span>
      </div>
    `;
  }).join('');

  // Keep track of current selected value to restore it
  const currentSelectVal = elSelectXmlModel.value;
  
  // Populate select model dropdown
  let optionsHtml = `<option value="auto">Auto-detect from LM Studio</option>`;
  models.forEach(model => {
    optionsHtml += `<option value="${model}">${model}</option>`;
  });
  elSelectXmlModel.innerHTML = optionsHtml;
  
  // Restore selection if it exists
  if (currentSelectVal && models.includes(currentSelectVal)) {
    elSelectXmlModel.value = currentSelectVal;
  }
}

// 2. Stream logs using SSE
function initializeSSE() {
  const eventSource = new EventSource('/api/logs');
  
  eventSource.onmessage = (event) => {
    try {
      const data = JSON.parse(event.data);
      
      if (data.type === 'history') {
        elLogsContainer.innerHTML = '';
        data.logs.forEach(log => appendLog(log));
      } else {
        // Remove placeholder if present
        const placeholder = elLogsContainer.querySelector('.log-placeholder');
        if (placeholder) placeholder.remove();
        
        appendLog(data);
      }
    } catch (err) {
      console.error('Error parsing SSE event:', err);
    }
  };
  
  eventSource.onerror = () => {
    console.warn('SSE connection lost. Reconnecting...');
    eventSource.close();
    setTimeout(initializeSSE, 3000);
  };
}

const isSystemLog = (log) => {
  // Disconnections / connection status checks/errors should always be shown (not suppressed)
  if (log.level === 'error') return false;
  
  // Initial model load/startup messages should always be shown
  if (log.type === 'server' && (log.message.includes('started') || log.message.includes('Dashboard:') || log.message.includes('Set RimWorld endpoint'))) {
    return false;
  }
  
  // Human-readable outgoing proxy request
  if (log.type === 'proxy' && log.level === 'info' && log.message.startsWith('User ➔')) {
    return false;
  }
  
  // Human-readable incoming proxy response (e.g., "PawnName ➔ User: ...", "Storyteller AI ➔ ...", "Advisor AI ➔ ...")
  if (log.type === 'proxy' && log.level === 'success') {
    const msg = log.message;
    if (msg.includes('➔ User') || msg.includes('Storyteller AI ➔') || msg.includes('Advisor AI ➔')) {
      return false;
    }
  }
  
  // All other logs are considered system/queue noise
  return true;
};

function formatLogMessage(message) {
  if (!message) return '';
  const match = message.match(/^\[(.*?)\]\s*([\s\S]*)$/);
  if (match) {
    const category = match[1];
    const rest = match[2];
    const categoryLower = category.toLowerCase().replace(/\s+/g, '-');
    return `<span class="log-module-badge module-${categoryLower}">${escapeHtml(category)}</span>${escapeHtml(rest)}`;
  }
  return escapeHtml(message);
}

function appendLog(log) {
  // Store log in map
  logsMap.set(log.id, log);
  
  const logItem = document.createElement('div');
  const isSys = isSystemLog(log);
  logItem.className = `log-item log-${log.level}${isSys ? ' log-item-sys' : ''}`;
  logItem.dataset.id = log.id;
  
  const timeStr = new Date(log.timestamp).toLocaleTimeString();
  
  logItem.innerHTML = `
    <div class="log-header">
      <div class="log-meta">
        <span class="log-time">${timeStr}</span>
        <span class="log-type">${log.type}</span>
      </div>
      <span class="log-chevron">🛈</span>
    </div>
    <div class="log-msg">${formatLogMessage(log.message)}</div>
  `;
  
  logItem.addEventListener('click', () => showLogDetails(log.id));
  
  elLogsContainer.appendChild(logItem);
  elLogsContainer.scrollTop = elLogsContainer.scrollHeight;
}

function escapeHtml(text) {
  if (!text) return '';
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

// Show Log Details in Modal
function showLogDetails(id) {
  const log = logsMap.get(id);
  if (!log) return;
  
  elDetailTime.textContent = new Date(log.timestamp).toLocaleString();
  elDetailType.textContent = log.type.toUpperCase();
  elDetailType.className = `value pill log-${log.level}`;
  
  elDetailDuration.textContent = log.details && log.details.durationMs 
    ? `${log.details.durationMs} ms` 
    : 'N/A';
    
  elDetailTokens.textContent = log.details && log.details.promptTokens !== undefined
    ? `Prompt: ${log.details.promptTokens} | Completion: ${log.details.completionTokens}`
    : 'N/A';
    
  if (log.details) {
    elDetailCode.textContent = JSON.stringify(log.details, null, 2);
    elDetailCode.parentElement.style.display = 'flex';
  } else {
    elDetailCode.parentElement.style.display = 'none';
  }
  
  elModalOverlay.classList.add('active');
}

// 3. Form config post
elConfigForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  
  const payload = {
    port: parseInt(elInputPort.value),
    lmStudioUrl: elInputUrl.value,
    bridgeApiKey: elInputBridgeKey.value,
    lmStudioApiKey: elInputLmKey.value,
    autoMapModel: elChkAutoMap.checked,
    sanitizeResponse: elChkSanitize.checked
  };
  
  try {
    const response = await fetch('/api/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    
    if (!response.ok) throw new Error('Failed to update config');
    
    alert('Configuration saved successfully! If you changed the port, you will need to restart the server.');
    updateStatus();
  } catch (err) {
    alert(`Error: ${err.message}`);
  }
});

// 4. Test connectivity
elBtnTestPing.addEventListener('click', async () => {
  try {
    elBtnTestPing.disabled = true;
    elBtnTestPing.textContent = 'Pinging...';
    
    const response = await fetch('/api/test-connection', { method: 'POST' });
    const data = await response.json();
    
    if (data.online) {
      alert(`Success! Connected to LM Studio.\nLoaded models count: ${data.models.length}`);
    } else {
      alert(`Connection failed: ${data.error || 'Unknown error'}`);
    }
  } catch (err) {
    alert(`Network Error: ${err.message}`);
  } finally {
    elBtnTestPing.disabled = false;
    elBtnTestPing.textContent = 'Ping LM Studio';
  }
});

// 5. Submit model test
elBtnSubmitTest.addEventListener('click', async () => {
  const promptText = elTxtPlaygroundPrompt.value.trim();
  if (!promptText) {
    alert('Please enter a prompt first.');
    return;
  }
  
  try {
    elBtnSubmitTest.disabled = true;
    elSpinnerTest.style.display = 'inline-block';
    elPlaygroundResultBox.style.display = 'none';
    
    // Query our own proxy completions endpoint using default test values
    const headers = { 'Content-Type': 'application/json' };
    if (elInputBridgeKey.value) {
      headers['Authorization'] = `Bearer ${elInputBridgeKey.value}`;
    }
    
    const response = await fetch('/v1/chat/completions', {
      method: 'POST',
      headers: headers,
      body: JSON.stringify({
        model: 'playground-test',
        messages: [
          { role: 'user', content: promptText }
        ]
      })
    });
    
    const data = await response.json();
    
    if (data.error) {
      throw new Error(data.error.message || 'API Completion error');
    }
    
    const content = data.choices && data.choices[0] && data.choices[0].message
      ? data.choices[0].message.content
      : JSON.stringify(data, null, 2);
      
    elPlaygroundOutput.textContent = content;
    elPlaygroundResultBox.style.display = 'flex';
  } catch (err) {
    alert(`Model call failed: ${err.message}`);
  } finally {
    elBtnSubmitTest.disabled = false;
    elSpinnerTest.style.display = 'none';
  }
});

// 6. RimWorld Integration UI Linker
const elBadgeRimworld = document.getElementById('badge-rimworld-status');
const elInputXmlFile = document.getElementById('input-xml-file');
const elSelectXmlModel = document.getElementById('select-xml-model');
const elXmlEndpoint = document.getElementById('lbl-xml-endpoint');
const elXmlKey = document.getElementById('lbl-xml-key');
const elBtnLinkRimworld = document.getElementById('btn-link-rimworld');

let initializedRimWorldXml = false;
let initializedRimWorldModel = false;

// Context Optimizer Elements
const elTokenWindow = document.getElementById('input-token-window');
const elBadgeContext = document.getElementById('badge-context-status');
const elBtnInjectContext = document.getElementById('btn-inject-context');
const elChkAutoRebalance = document.getElementById('chk-auto-rebalance');
const elChkLinkModelContext = document.getElementById('chk-link-model-context');
const elTotalChars = document.getElementById('lbl-total-chars');
const elRebalanceStatus = document.getElementById('lbl-rebalance-status');

const elInputScene = document.getElementById('input-scene-chars');
const elSliderScene = document.getElementById('slider-scene-chars');
const elLblSceneTokens = document.getElementById('lbl-scene-tokens');

const elInputHistory = document.getElementById('input-history-chars');
const elSliderHistory = document.getElementById('slider-history-chars');
const elLblHistoryTokens = document.getElementById('lbl-history-tokens');

const elInputMemory = document.getElementById('input-memory-chars');
const elSliderMemory = document.getElementById('slider-memory-chars');
const elLblMemoryTokens = document.getElementById('lbl-memory-tokens');

const elInputSummary = document.getElementById('input-summary-chars');
const elSliderSummary = document.getElementById('slider-summary-chars');
const elLblSummaryTokens = document.getElementById('lbl-summary-tokens');

const elSegScene = document.querySelector('.segment-scene');
const elSegHistory = document.querySelector('.segment-history');
const elSegMemory = document.querySelector('.segment-memory');
const elSegSummary = document.querySelector('.segment-summary');

async function updateRimWorldConfigStatus() {
  try {
    const customPath = elInputXmlFile ? elInputXmlFile.value.trim() : '';
    const url = customPath ? `/api/rimworld-config?filePath=${encodeURIComponent(customPath)}` : '/api/rimworld-config';
    
    const response = await fetch(url);
    if (!response.ok) throw new Error('Failed to fetch RimWorld status');
    const data = await response.json();
    
    // Set default file path in input if empty or first load
    if (!initializedRimWorldXml && data.filePath) {
      elInputXmlFile.value = data.filePath;
      initializedRimWorldXml = true;
    }
    
    if (data.found) {
      elXmlEndpoint.textContent = data.apiEndpoint || 'Not Set';
      elXmlEndpoint.title = data.apiEndpoint || '';
      elXmlKey.textContent = data.apiKey ? '••••••••' : 'Not Set';
      elXmlKey.title = data.apiKey || '';
      
      if (!initializedRimWorldModel && data.modelName) {
        elSelectXmlModel.value = data.modelName;
        initializedRimWorldModel = true;
      }
      
      // Determine if endpoints are pointing to the bridge (with or without /v1 and trailing slash)
      const currentPort = window.location.port || '3000';
      const cleanEndpoint = (data.apiEndpoint || '').replace(/\/+$/, '');
      const expectedBase1 = `https://localhost:${currentPort}`;
      const expectedBase2 = `http://localhost:${currentPort}`;
      
      const isLinked = (cleanEndpoint === expectedBase1 || 
                        cleanEndpoint === `${expectedBase1}/v1` || 
                        cleanEndpoint === expectedBase2 || 
                        cleanEndpoint === `${expectedBase2}/v1`) &&
                       data.apiKey !== '' &&
                       data.modelName !== '';
                       
      if (isLinked) {
        elBadgeRimworld.textContent = 'Linked';
        elBadgeRimworld.className = 'status-badge status-linked';
        elBtnLinkRimworld.disabled = false;
        elBtnLinkRimworld.querySelector('span').textContent = 'Update Settings';
      } else {
        elBadgeRimworld.textContent = 'Not Linked';
        elBadgeRimworld.className = 'status-badge status-unlinked';
        elBtnLinkRimworld.disabled = false;
        elBtnLinkRimworld.querySelector('span').textContent = 'Inject Settings';
      }
    } else {
      elBadgeRimworld.textContent = 'Offline';
      elBadgeRimworld.className = 'status-badge status-unlinked';
      elXmlEndpoint.textContent = 'Not Created';
      elXmlKey.textContent = 'Not Created';
      elBtnLinkRimworld.disabled = false;
      elBtnLinkRimworld.querySelector('span').textContent = 'Inject Settings';
    }
  } catch (err) {
    console.error('Error fetching RimWorld link status:', err);
  }
}

// Re-read file configuration when user changes the path manually
elInputXmlFile.addEventListener('change', () => {
  initializedRimWorldXml = true;
  initializedRimWorldModel = false;
  updateRimWorldConfigStatus();
});

elBtnLinkRimworld.addEventListener('click', async () => {
  if (!confirm('Are you sure you want to inject the bridge configuration (API Endpoint, API Key, and Model Name) into your RimWorld mod settings XML file?')) {
    return;
  }

  try {
    elBtnLinkRimworld.disabled = true;
    elBtnLinkRimworld.querySelector('span').textContent = 'Injecting...';
    
    const payload = {
      filePath: elInputXmlFile.value.trim(),
      modelName: elSelectXmlModel.value
    };
    
    const response = await fetch('/api/rimworld-config', { 
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    const data = await response.json();
    
    if (data.success) {
      alert('RimWorld mod settings file successfully injected! Please exit and restart RimWorld if the game is currently running.');
      initializedRimWorldModel = false;
      updateRimWorldConfigStatus();
    } else {
      alert(`Injection failed: ${data.message}`);
    }
  } catch (err) {
    alert(`Error injecting settings: ${err.message}`);
  } finally {
    updateRimWorldConfigStatus();
  }
});

// 7. Utility handlers
elBtnClearLogs.addEventListener('click', () => {
  elLogsContainer.innerHTML = `<div class="log-placeholder">Activity logs cleared.</div>`;
  logsMap.clear();
});

elBtnRefreshModels.addEventListener('click', () => {
  initializedContext = false;
  updateStatus();
  updateRimWorldConfigStatus();
  updateContextStatus();
});

elBtnCloseModal.addEventListener('click', () => {
  elModalOverlay.classList.remove('active');
});

elModalOverlay.addEventListener('click', (e) => {
  if (e.target === elModalOverlay) {
    elModalOverlay.classList.remove('active');
  }
});

// 8. Context Optimizer Logic
function recalculateAllocations() {
  const isLocked = elChkAutoRebalance.checked;
  
  elInputScene.disabled = isLocked;
  elSliderScene.disabled = isLocked;
  
  elInputHistory.disabled = isLocked;
  elSliderHistory.disabled = isLocked;
  
  elInputMemory.disabled = isLocked;
  elSliderMemory.disabled = isLocked;
  
  elInputSummary.disabled = isLocked;
  elSliderSummary.disabled = isLocked;

  if (isLocked) {
    const tokens = parseInt(elTokenWindow.value) || 34400;
    const totalChars = tokens * 4;
    
    const sceneChars = Math.round(totalChars * 0.20);
    const totalDialogueChars = Math.round(totalChars * 0.50);
    const memoryChars = Math.round(totalChars * 0.20);
    const summaryChars = Math.round(totalChars * 0.10);
    
    // Sync inputs & sliders
    elInputScene.value = sceneChars;
    elSliderScene.value = sceneChars;
    
    elInputHistory.value = totalDialogueChars;
    elSliderHistory.value = totalDialogueChars;
    
    elInputMemory.value = memoryChars;
    elSliderMemory.value = memoryChars;
    
    elInputSummary.value = summaryChars;
    elSliderSummary.value = summaryChars;
    
    // Update widths
    elSegScene.style.width = '20%';
    elSegHistory.style.width = '50%';
    elSegMemory.style.width = '20%';
    elSegSummary.style.width = '10%';
    
    // Update hover titles
    elSegScene.title = `Pawn & Scene: ${sceneChars.toLocaleString()} chars`;
    elSegHistory.title = `Dialogue History: ${totalDialogueChars.toLocaleString()} chars`;
    elSegMemory.title = `Memory/Incident Log: ${memoryChars.toLocaleString()} chars`;
    elSegSummary.title = `Timeline Summary: ${summaryChars.toLocaleString()} chars`;
    
    // Update total characters text
    elTotalChars.textContent = totalChars.toLocaleString();
    
    // Status text
    elRebalanceStatus.textContent = 'Ratios aligned with recommended amounts.';
    elRebalanceStatus.className = 'rebalance-status';
  } else {
    // Read individual inputs
    const sceneChars = parseInt(elInputScene.value) || 0;
    const totalDialogueChars = parseInt(elInputHistory.value) || 0;
    const memoryChars = parseInt(elInputMemory.value) || 0;
    const summaryChars = parseInt(elInputSummary.value) || 0;
    
    // Sync sliders
    elSliderScene.value = sceneChars;
    elSliderHistory.value = totalDialogueChars;
    elSliderMemory.value = memoryChars;
    elSliderSummary.value = summaryChars;
    
    const totalChars = sceneChars + totalDialogueChars + memoryChars + summaryChars;
    
    // Update master token window display
    const estimatedTokens = Math.round(totalChars / 4);
    elTokenWindow.value = estimatedTokens;
    
    // Update visual segments proportionally
    if (totalChars > 0) {
      elSegScene.style.width = `${(sceneChars / totalChars * 100)}%`;
      elSegHistory.style.width = `${(totalDialogueChars / totalChars * 100)}%`;
      elSegMemory.style.width = `${(memoryChars / totalChars * 100)}%`;
      elSegSummary.style.width = `${(summaryChars / totalChars * 100)}%`;
    }
    
    // Update hover titles
    elSegScene.title = `Pawn & Scene: ${sceneChars.toLocaleString()} chars`;
    elSegHistory.title = `Dialogue History: ${totalDialogueChars.toLocaleString()} chars`;
    elSegMemory.title = `Memory/Incident Log: ${memoryChars.toLocaleString()} chars`;
    elSegSummary.title = `Timeline Summary: ${summaryChars.toLocaleString()} chars`;
    
    // Update total characters text
    elTotalChars.textContent = totalChars.toLocaleString();
    
    // Check if it matches exactly
    const isExact = (totalChars > 0 && 
                     Math.abs((sceneChars / totalChars) - 0.20) < 0.01 && 
                     Math.abs((totalDialogueChars / totalChars) - 0.50) < 0.01 && 
                     Math.abs((memoryChars / totalChars) - 0.20) < 0.01 && 
                     Math.abs((summaryChars / totalChars) - 0.10) < 0.01);
                     
    if (isExact) {
      elRebalanceStatus.textContent = 'Ratios aligned with recommended amounts.';
      elRebalanceStatus.className = 'rebalance-status';
    } else {
      elRebalanceStatus.textContent = 'Custom allocations locked in.';
      elRebalanceStatus.className = 'rebalance-status custom';
    }
  }
  
  // Recalculate and update sub-token indicators
  const sceneVal = parseInt(elInputScene.value) || 0;
  const historyVal = parseInt(elInputHistory.value) || 0;
  const memoryVal = parseInt(elInputMemory.value) || 0;
  const summaryVal = parseInt(elInputSummary.value) || 0;
  
  elLblSceneTokens.textContent = `≈ ${Math.round(sceneVal / 4).toLocaleString()} tokens`;
  elLblHistoryTokens.textContent = `≈ ${Math.round(historyVal / 4).toLocaleString()} tokens`;
  elLblMemoryTokens.textContent = `≈ ${Math.round(memoryVal / 4).toLocaleString()} tokens`;
  elLblSummaryTokens.textContent = `≈ ${Math.round(summaryVal / 4).toLocaleString()} tokens`;
}

function setupSyncListeners(inputEl, sliderEl) {
  const sync = (source, target) => {
    target.value = source.value;
    elChkAutoRebalance.checked = false; // Turn off locking on manual tweak
    recalculateAllocations();
  };
  
  inputEl.addEventListener('input', () => sync(inputEl, sliderEl));
  sliderEl.addEventListener('input', () => sync(sliderEl, inputEl));
}

// Attach sync behavior
setupSyncListeners(elInputScene, elSliderScene);
setupSyncListeners(elInputHistory, elSliderHistory);
setupSyncListeners(elInputMemory, elSliderMemory);
setupSyncListeners(elInputSummary, elSliderSummary);

async function updateContextStatus() {
  try {
    const response = await fetch('/api/rimworld-context');
    if (!response.ok) throw new Error('Failed to fetch context status');
    const data = await response.json();
    
    if (data.found) {
      if (data.noPresets) {
        elBadgeContext.textContent = 'No Presets';
        elBadgeContext.className = 'status-badge status-unlinked';
        elBtnInjectContext.disabled = true;
      } else {
        elBadgeContext.textContent = 'Linked';
        elBadgeContext.className = 'status-badge status-linked';
        elBtnInjectContext.disabled = false;
        
        // Update inputs only on initial load, or if not user-edited
        if (data.maxTotalChars && !initializedContext) {
          elInputScene.value = data.maxSceneChars || 0;
          elInputHistory.value = data.maxTotalChars || 0;
          elInputMemory.value = data.maxInjectedChars || 0;
          elInputSummary.value = data.summaryCharBudget || 0;
          
          const total = (data.maxSceneChars || 0) + (data.maxTotalChars || 0) + (data.maxInjectedChars || 0) + (data.summaryCharBudget || 0);
          const isExact = (total > 0 && 
                           Math.abs((data.maxSceneChars / total) - 0.20) < 0.01 && 
                           Math.abs((data.maxTotalChars / total) - 0.50) < 0.01 && 
                           Math.abs((data.maxInjectedChars / total) - 0.20) < 0.01 && 
                           Math.abs((data.summaryCharBudget / total) - 0.10) < 0.01);
                           
          elChkAutoRebalance.checked = isExact;
          if (isExact) {
            const estimatedTokens = Math.round(total / 4);
            elTokenWindow.value = estimatedTokens;
          } else {
            elRebalanceStatus.textContent = 'Custom allocations loaded from presets.';
            elRebalanceStatus.className = 'rebalance-status custom';
          }
          
          initializedContext = true;
          recalculateAllocations();
        }
      }
    } else {
      elBadgeContext.textContent = 'Offline';
      elBadgeContext.className = 'status-badge status-unlinked';
      elBtnInjectContext.disabled = true;
    }
  } catch (err) {
    console.error('Error fetching context presets status:', err);
  }
}

elTokenWindow.addEventListener('input', () => {
  elTokenWindow.dataset.userEdited = 'true';
  recalculateAllocations();
});

elChkAutoRebalance.addEventListener('change', () => {
  recalculateAllocations();
});

elChkLinkModelContext.addEventListener('change', () => {
  localStorage.setItem('linkModelContext', elChkLinkModelContext.checked);
  
  if (elChkLinkModelContext.checked) {
    elTokenWindow.disabled = true;
    elTokenWindow.title = "Context window is automatically linked to your active loaded model in LM Studio.";
    elChkAutoRebalance.checked = true;
    elChkAutoRebalance.disabled = true;
    elChkAutoRebalance.title = "Ratios are locked to recommended values when synced with the loaded model.";
    recalculateAllocations(); // Re-balance immediately
    updateStatus(); // Sync immediately from server
  } else {
    elTokenWindow.disabled = false;
    elTokenWindow.title = "";
    elChkAutoRebalance.disabled = false;
    elChkAutoRebalance.title = "";
    recalculateAllocations();
  }
});

elBtnInjectContext.addEventListener('click', async () => {
  const sceneChars = parseInt(elInputScene.value) || 0;
  const totalDialogueChars = parseInt(elInputHistory.value) || 0;
  const memoryChars = parseInt(elInputMemory.value) || 0;
  const summaryChars = parseInt(elInputSummary.value) || 0;
  
  const payload = {
    maxSceneChars: sceneChars,
    maxTotalChars: totalDialogueChars,
    maxInjectedChars: memoryChars,
    summaryCharBudget: summaryChars
  };
  
  if (!confirm(`Are you sure you want to inject these optimized character budgets into your RimWorld mod presets?\n\n- Pawn & Scene State: ${sceneChars.toLocaleString()} chars\n- Dialogue History: ${totalDialogueChars.toLocaleString()} chars\n- Long-Term Memory: ${memoryChars.toLocaleString()} chars\n- Timeline Summary: ${summaryChars.toLocaleString()} chars`)) {
    return;
  }
  
  try {
    elBtnInjectContext.disabled = true;
    elBtnInjectContext.querySelector('span').textContent = 'Injecting...';
    
    const response = await fetch('/api/rimworld-context', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    
    const data = await response.json();
    if (data.success) {
      alert('Context allocations successfully injected into your RimWorld mod presets!');
      initializedContext = false;
      updateContextStatus();
    } else {
      alert(`Injection failed: ${data.message}`);
    }
  } catch (err) {
    alert(`Error injecting context: ${err.message}`);
  } finally {
    elBtnInjectContext.disabled = false;
    elBtnInjectContext.querySelector('span').textContent = 'Inject Context Budgets';
  }
});

// Load linkModelContext toggle state from localStorage
const storedLinkContext = localStorage.getItem('linkModelContext');
if (storedLinkContext === 'true') {
  elChkLinkModelContext.checked = true;
  elTokenWindow.disabled = true;
  elTokenWindow.title = "Context window is automatically linked to your active loaded model in LM Studio.";
  elChkAutoRebalance.checked = true;
  elChkAutoRebalance.disabled = true;
  elChkAutoRebalance.title = "Ratios are locked to recommended values when synced with the loaded model.";
} else {
  elChkLinkModelContext.checked = false;
  elTokenWindow.disabled = false;
  elChkAutoRebalance.disabled = false;
}

// System logs show/hide filtering
function updateSysLogsVisibility() {
  if (elChkShowSysLogs.checked) {
    elLogsContainer.classList.remove('hide-sys-logs');
  } else {
    elLogsContainer.classList.add('hide-sys-logs');
  }
  elLogsContainer.scrollTop = elLogsContainer.scrollHeight;
}
elChkShowSysLogs.addEventListener('change', updateSysLogsVisibility);
elChkShowSysLogs.checked = false;
updateSysLogsVisibility();

// Recommendation calculation variables
let currentAvgDurationMs = 0;
let currentTrackedPawnsCount = 0;

function recalculateScalabilityRecommendations() {
  const simPawns = parseInt(elSimPawnCount.value) || 1;
  const tps = parseInt(elSimTpsSlider.value) || 60;
  
  // Use average response time or fall back to 10s if we don't have enough data
  const latencySecs = currentAvgDurationMs > 0 ? (currentAvgDurationMs / 1000) : 10;
  
  // Formulas:
  // Pawn Dialogue: Pawns * Latency * TPS
  const pawnTicks = Math.max(100, Math.round(simPawns * latencySecs * tps));
  // Storyteller: 2 * Latency * TPS
  const storytellerTicks = Math.max(100, Math.round(2 * latencySecs * tps));
  // Advisor: 1.5 * Latency * TPS
  const advisorTicks = Math.max(100, Math.round(1.5 * latencySecs * tps));
  // Colony Summarizer: 3 * Latency * TPS
  const summaryTicks = Math.max(100, Math.round(3 * latencySecs * tps));
  
  // Update HTML text content
  elXmlPawnTicks.textContent = pawnTicks.toLocaleString();
  elXmlStorytellerTicks.textContent = storytellerTicks.toLocaleString();
  elXmlAdvisorTicks.textContent = advisorTicks.toLocaleString();
  elXmlSummaryTicks.textContent = summaryTicks.toLocaleString();
  
  // Safety checks (just simple verification)
  const verifySafety = (badgeId, actualTicks, minTicks) => {
    const badge = document.getElementById(badgeId);
    if (!badge) return;
    if (actualTicks >= minTicks) {
      badge.textContent = 'Safe';
      badge.className = 'status-badge status-linked';
    } else {
      badge.textContent = 'Overlap Risk';
      badge.className = 'status-badge status-unlinked';
    }
  };
  
  verifySafety('pawn-safety-badge', pawnTicks, 600);
  verifySafety('storyteller-safety-badge', storytellerTicks, 600);
  verifySafety('advisor-safety-badge', advisorTicks, 600);
  verifySafety('summary-safety-badge', summaryTicks, 600);
}

// Attach simulation listeners
elSimPawnCount.addEventListener('input', () => {
  elSimPawnCount.dataset.userEdited = 'true';
  recalculateScalabilityRecommendations();
});

elSimTpsSlider.addEventListener('input', () => {
  const tps = elSimTpsSlider.value;
  elLblSimTps.textContent = `${tps} TPS`;
  
  // Deactivate all preset buttons
  document.querySelectorAll('.tps-preset-btn').forEach(btn => {
    btn.classList.remove('active');
  });
  
  // Check if it matches a preset
  const matchingPreset = document.querySelector(`.tps-preset-btn[data-tps="${tps}"]`);
  if (matchingPreset) {
    matchingPreset.classList.add('active');
  }
  
  recalculateScalabilityRecommendations();
});

// Preset buttons
document.querySelectorAll('.tps-preset-btn').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.tps-preset-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    
    const tps = btn.dataset.tps;
    elSimTpsSlider.value = tps;
    elLblSimTps.textContent = `${tps} TPS`;
    recalculateScalabilityRecommendations();
  });
});

// Collapsible Advanced Context Optimizer panel toggle
const elBtnToggleContextOptimizer = document.getElementById('btn-toggle-context-optimizer');
const elContextCardMain = document.querySelector('.context-card-main');

elBtnToggleContextOptimizer.addEventListener('click', (e) => {
  if (e.target.id === 'badge-context-status' || e.target.classList.contains('status-badge')) {
    return;
  }
  elContextCardMain.classList.toggle('collapsed');
});

// =====================================================
// DATABASE PANELS
// =====================================================

// Collapsible Pawn List toggle
const elBtnTogglePawnList = document.getElementById('btn-toggle-pawn-list');
const elPawnListCard = document.getElementById('pawn-list-card');

elBtnTogglePawnList.addEventListener('click', (e) => {
  if (e.target.classList.contains('status-badge')) return;
  elPawnListCard.classList.toggle('collapsed');
});

// DB Stats DOM elements
const elDbSize = document.getElementById('db-size');
const elDbSchemaVersion = document.getElementById('db-schema-version');
const elDbTotalPawns = document.getElementById('db-total-pawns');
const elDbTotalMemories = document.getElementById('db-total-memories');
const elDbTotalRelationships = document.getElementById('db-total-relationships');
const elDbTotalThreads = document.getElementById('db-total-threads');
const elDbTotalInteractions = document.getElementById('db-total-interactions');

// Colony DOM elements
const elColonyContent = document.getElementById('colony-content');
const elBadgeColonyStatus = document.getElementById('badge-colony-status');

// Pawn list DOM elements
const elPawnListBody = document.getElementById('pawn-list-body');
const elBadgePawnCount = document.getElementById('badge-pawn-count');

// Memory feed DOM elements
const elMemoryFeedBody = document.getElementById('memory-feed-body');

// Weight heatmap DOM elements
const elHeatmapWeightBar = document.getElementById('heatmap-weight-bar');
const elHeatmapWeightLegend = document.getElementById('heatmap-weight-legend');
const elHeatmapThreadBar = document.getElementById('heatmap-thread-bar');
const elHeatmapThreadLegend = document.getElementById('heatmap-thread-legend');

// Threads DOM elements
const elThreadsBody = document.getElementById('threads-body');

// Mod list DOM elements
const elModList = document.getElementById('mod-list');

// Track active colony ID for sub-queries
let activeColonyId = null;

function formatBytes(bytes) {
  if (bytes == null || bytes === 0) return '0 B';
  if (bytes < 1024) return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
  return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
}

function getMemoryTypeBadgeClass(type) {
  const t = (type || '').toLowerCase();
  if (t.includes('raid')) return 'type-raid';
  if (t.includes('social')) return 'type-social';
  if (t.includes('event')) return 'type-event';
  if (t.includes('trade')) return 'type-trade';
  if (t.includes('quest')) return 'type-quest';
  return 'type-default';
}

function getWeightClass(weight) {
  if (weight < 0.2) return 'weight-critical';
  if (weight < 0.5) return 'weight-low';
  if (weight < 0.8) return 'weight-medium';
  return 'weight-high';
}

function getTraitClass(degree) {
  if (degree > 0) return 'trait-positive';
  if (degree < 0) return 'trait-negative';
  return 'trait-neutral';
}

function renderStackedBar(container, legendContainer, segments) {
  const total = segments.reduce((sum, s) => sum + s.count, 0);
  if (total === 0) {
    container.innerHTML = '<div class="db-empty-state" style="padding: 0.35rem 1rem; font-size: 0.72rem;">No data</div>';
    legendContainer.innerHTML = '';
    return;
  }

  container.innerHTML = segments.map(s => {
    const pct = (s.count / total) * 100;
    if (s.count === 0) return '';
    return `<div class="stacked-segment seg-${s.key}" style="flex: ${s.count};" title="${s.label}: ${s.count}">
      <span class="segment-label">${pct > 12 ? s.count : ''}</span>
    </div>`;
  }).join('');

  legendContainer.innerHTML = segments.map(s => `
    <div class="heatmap-legend-item">
      <span class="heatmap-legend-dot dot-${s.key}"></span>
      <span>${s.label}: <strong>${s.count}</strong></span>
    </div>
  `).join('');
}

async function updateDatabaseStats() {
  try {
    const response = await fetch('/api/database/stats');
    if (!response.ok) return;
    const data = await response.json();

    // --- Sidebar: DB Stats ---
    elDbSchemaVersion.textContent = data.schema_version ? `v${data.schema_version}` : '-';
    elDbSize.textContent = formatBytes(data.db_size_bytes);

    if (data.totals) {
      elDbTotalPawns.textContent = (data.totals.pawns || 0).toLocaleString();
      elDbTotalMemories.textContent = (data.totals.memories || 0).toLocaleString();
      elDbTotalRelationships.textContent = (data.totals.relationships || 0).toLocaleString();
      elDbTotalThreads.textContent = (data.totals.threads || 0).toLocaleString();
      elDbTotalInteractions.textContent = (data.totals.interactions || 0).toLocaleString();
    }

    // --- Colony Overview ---
    const colonies = data.colonies || [];
    if (colonies.length > 0) {
      const colony = colonies[0]; // Show first/active colony
      activeColonyId = colony.id;

      elBadgeColonyStatus.textContent = 'Active';
      elBadgeColonyStatus.className = 'status-badge status-linked';

      elColonyContent.innerHTML = `
        <div class="colony-header-info">
          <span class="colony-name">${escapeHtml(colony.colony_name || 'Unnamed Colony')}</span>
          <div class="colony-meta">
            <span class="meta-badge">🌍 ${escapeHtml(colony.biome || 'Unknown')}</span>
            <span class="meta-badge">⛪ ${escapeHtml(colony.ideology_name || 'None')}</span>
          </div>
        </div>
        <div class="colony-stats-row">
          <div class="capacity-stat-box">
            <span class="stat-label">Pawns</span>
            <span class="stat-value">${colony.pawn_count || 0}</span>
          </div>
          <div class="capacity-stat-box">
            <span class="stat-label">Memories</span>
            <span class="stat-value">${colony.memory_count || 0}</span>
          </div>
          <div class="capacity-stat-box">
            <span class="stat-label">Relationships</span>
            <span class="stat-value">${colony.relationship_count || 0}</span>
          </div>
          <div class="capacity-stat-box">
            <span class="stat-label">Active Threads</span>
            <span class="stat-value">${colony.active_threads || 0}</span>
          </div>
        </div>
      `;
    } else {
      activeColonyId = null;
      elBadgeColonyStatus.textContent = 'No Colony';
      elBadgeColonyStatus.className = 'status-badge';
      elColonyContent.innerHTML = '<div class="db-empty-state">No colony data available. Start a game with RimSynapse active.</div>';
    }

    // --- Weight Heatmap ---
    if (data.weight_distribution) {
      const wd = data.weight_distribution;
      renderStackedBar(elHeatmapWeightBar, elHeatmapWeightLegend, [
        { key: 'decaying', label: 'Decaying', count: wd.decaying || 0 },
        { key: 'stable', label: 'Stable', count: wd.stable || 0 },
        { key: 'active', label: 'Active', count: wd.active || 0 },
        { key: 'hot', label: 'Hot', count: wd.hot || 0 }
      ]);
    }

    if (data.thread_distribution) {
      const td = data.thread_distribution;
      renderStackedBar(elHeatmapThreadBar, elHeatmapThreadLegend, [
        { key: 'fading', label: 'Fading', count: td.fading || 0 },
        { key: 'active', label: 'Active', count: td.active || 0 },
        { key: 'hot', label: 'Hot', count: td.hot || 0 }
      ]);
    }

    // --- Sidebar: Mod Registry ---
    const mods = data.mods || [];
    if (mods.length > 0) {
      elModList.innerHTML = mods.map(mod => `
        <div class="mod-item" title="Registered: ${mod.registered_at || ''}">
          <span class="mod-id">${escapeHtml(mod.mod_id || '-')}</span>
          <span class="mod-version">${escapeHtml(mod.version || '-')}</span>
          <span class="mod-tables">${(mod.table_names || []).length} tables</span>
        </div>
      `).join('');
    } else {
      elModList.innerHTML = '<div class="db-empty-state">No mods registered</div>';
    }

    // --- Fetch sub-resources for active colony ---
    if (activeColonyId) {
      fetchPawns(activeColonyId);
      fetchThreads(activeColonyId);
    }

    // Always fetch recent memories (not colony-specific)
    fetchRecentMemories();

  } catch (err) {
    console.error('Error fetching database stats:', err);
  }
}

async function fetchPawns(colonyId) {
  try {
    const response = await fetch(`/api/database/pawns?colony_id=${colonyId}`);
    if (!response.ok) return;
    const pawns = await response.json();

    elBadgePawnCount.textContent = `${pawns.length} Pawn${pawns.length !== 1 ? 's' : ''}`;

    if (pawns.length === 0) {
      elPawnListBody.innerHTML = '<div class="db-empty-state">No pawns found</div>';
      return;
    }

    elPawnListBody.innerHTML = pawns.map(pawn => {
      const genderIcon = pawn.gender === 'Male' ? '♂️' : pawn.gender === 'Female' ? '♀️' : '⚧';
      const displayName = pawn.name_nick || pawn.name_first || 'Unknown';
      const traits = (pawn.traits || []);

      const traitPills = traits.map(t => {
        const cls = getTraitClass(t.degree || 0);
        return `<span class="trait-pill ${cls}">${escapeHtml(t.label || 'Unknown')}</span>`;
      }).join('');

      return `
        <div class="pawn-row">
          <span class="pawn-icon">${genderIcon}</span>
          <div class="pawn-info">
            <div class="pawn-name-row">
              <span class="pawn-name">${escapeHtml(displayName)}</span>
              <span class="pawn-age">${pawn.age_biological || '?'}y</span>
            </div>
            ${traitPills ? `<div class="pawn-traits">${traitPills}</div>` : ''}
          </div>
          <div class="pawn-stats">
            <span class="pawn-stat-chip"><span class="chip-icon">💬</span>${pawn.relationship_count || 0}</span>
            <span class="pawn-stat-chip"><span class="chip-icon">🧠</span>${pawn.memory_count || 0}</span>
          </div>
        </div>
      `;
    }).join('');
  } catch (err) {
    console.error('Error fetching pawns:', err);
  }
}

async function fetchRecentMemories() {
  try {
    const response = await fetch('/api/database/memories/recent?limit=20');
    if (!response.ok) return;
    const memories = await response.json();

    if (!memories || memories.length === 0) {
      elMemoryFeedBody.innerHTML = '<div class="db-empty-state">No memories recorded yet</div>';
      return;
    }

    elMemoryFeedBody.innerHTML = memories.map(mem => {
      const weight = parseFloat(mem.weight) || 0;
      const weightPct = Math.min(weight * 100, 100);
      const weightClass = getWeightClass(weight);
      const typeClass = getMemoryTypeBadgeClass(mem.memory_type);
      const timeStr = mem.created_at ? new Date(mem.created_at).toLocaleString() : '';

      return `
        <div class="memory-item">
          <div class="memory-header">
            <div class="memory-header-left">
              <span class="memory-pawn-name">${escapeHtml(mem.pawn_name || 'Unknown')}</span>
              <span class="memory-type-badge ${typeClass}">${escapeHtml(mem.memory_type || 'memory')}</span>
            </div>
            <span class="memory-time">${timeStr}</span>
          </div>
          <div class="memory-summary">${escapeHtml(mem.summary || '')}</div>
          <div class="memory-weight-row">
            <div class="memory-weight-bar">
              <div class="memory-weight-fill ${weightClass}" style="width: ${weightPct}%;"></div>
            </div>
            <span class="memory-weight-label">${weight.toFixed(2)}</span>
          </div>
        </div>
      `;
    }).join('');
  } catch (err) {
    console.error('Error fetching recent memories:', err);
  }
}

async function fetchThreads(colonyId) {
  try {
    const response = await fetch(`/api/database/threads?colony_id=${colonyId}`);
    if (!response.ok) return;
    const threads = await response.json();

    if (!threads || threads.length === 0) {
      elThreadsBody.innerHTML = '<div class="db-empty-state">No active narrative threads</div>';
      return;
    }

    elThreadsBody.innerHTML = threads.map(thread => {
      const weight = parseFloat(thread.weight) || 0;
      const weightPct = Math.min(weight * 100, 100);
      const weightClass = getWeightClass(weight);
      const resolvedClass = thread.is_resolved ? 'resolved' : 'active';
      const resolvedText = thread.is_resolved ? 'Resolved' : 'Active';

      return `
        <div class="thread-item">
          <div class="thread-header">
            <span class="thread-keyword">${escapeHtml(thread.keyword || '-')}</span>
            ${thread.category ? `<span class="thread-category">${escapeHtml(thread.category)}</span>` : ''}
            <span class="thread-resolved-badge ${resolvedClass}">${resolvedText}</span>
          </div>
          ${thread.description ? `<div class="thread-description">${escapeHtml(thread.description)}</div>` : ''}
          <div class="thread-footer">
            <div class="memory-weight-row" style="flex: 1;">
              <div class="memory-weight-bar">
                <div class="memory-weight-fill ${weightClass}" style="width: ${weightPct}%;"></div>
              </div>
              <span class="memory-weight-label">${weight.toFixed(2)}</span>
            </div>
            <span class="thread-refs">Referenced: ${thread.times_referenced || 0}×</span>
          </div>
        </div>
      `;
    }).join('');
  } catch (err) {
    console.error('Error fetching threads:', err);
  }
}

// Startup
updateStatus();
updateRimWorldConfigStatus();
updateContextStatus();
updateDatabaseStats();
recalculateAllocations();
initializeSSE();

// Poll state every 3s
setInterval(() => {
  updateStatus();
  updateRimWorldConfigStatus();
  updateContextStatus();
  updateDatabaseStats();
}, 3000);

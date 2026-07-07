$counters = Get-Counter -Counter '\\GPU Process Memory(*)\Dedicated Usage' -ErrorAction SilentlyContinue
if ($counters) {
    $results = @()
    foreach ($s in $counters.CounterSamples) {
        if ($s.CookedValue -gt 1MB) {
            $start = $s.Path.IndexOf("(")
            $end = $s.Path.IndexOf(")")
            if ($start -ge 0 -and $end -gt $start) {
                $instance = $s.Path.Substring($start + 1, $end - $start - 1)
                if ($instance -like "pid_*") {
                    $parts = $instance.Split("_")
                    if ($parts.Count -gt 1) {
                        $targetPid = $parts[1]
                        $proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
                        if ($proc -and ($proc.Name -like "*RimWorld*" -or $proc.Name -like "*LM Studio*" -or $proc.Name -like "*llama*")) {
                            $exists = $false
                            foreach ($r in $results) {
                                if ($r.Pid -eq $targetPid) { $exists = $true; break }
                            }
                            if (-not $exists) {
                                $results += [PSCustomObject]@{
                                    Pid = $targetPid
                                    Name = $proc.Name
                                    VramMB = [math]::round($s.CookedValue / 1MB, 2)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    $results | ConvertTo-Json -Compress
} else {
    Write-Host "[]"
}
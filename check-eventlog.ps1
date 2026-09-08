$ErrorActionPreference = 'SilentlyContinue'
$events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddMinutes(-20) } -MaxEvents 30
foreach ($event in $events) {
    if ($event.Message -match 'AiDataGateway' -or $event.ProviderName -match '\.NET') {
        Write-Output ('--- ' + $event.TimeCreated.ToString('HH:mm:ss') + ' [' + $event.ProviderName + '] level=' + $event.Level)
        Write-Output ($event.Message.Substring(0, [Math]::Min(900, $event.Message.Length)))
    }
}

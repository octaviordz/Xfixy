$result = 0 # Ok.
$connectionProfile = Get-NetConnectionProfile |
    Where-Object -FilterScript {
        ($_.InterfaceAlias -eq "Wi-Fi") -and
        ($_.IPv4Connectivity -eq "Internet") -or
        ($_.IPv6Connectivity -eq "Internet")
    } |
    Select-Object -First 1
# Write-Output $connectionProfile
$profileName = $connectionProfile.Name
if ($null -eq $connectionProfile) {
    $result = "Not connected."
} elseif (-Not(Test-Connection yahoo.com -Quiet -Count 1)) {
    $result = netsh wlan connect name=$profileName
}
Write-Output ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
Write-Output $result
# XT1687 4058
# HOME-8BA8

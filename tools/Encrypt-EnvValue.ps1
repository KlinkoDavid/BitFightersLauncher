param(
    [Parameter(Mandatory = $true)]
    [string]$Value
)

$bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
$protected = [System.Security.Cryptography.ProtectedData]::Protect(
    $bytes,
    $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser
)

$base64 = [Convert]::ToBase64String($protected)
Write-Output "enc:$base64"

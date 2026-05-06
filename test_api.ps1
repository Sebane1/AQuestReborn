try { 
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    $response = Invoke-WebRequest -Uri 'https://ai.hubujubu.com:5696/' -Method Post -Body '{"sender":"stel9","message":"Hello","aiName":"CustomNPC","modelChoice":""}' -ContentType 'application/json'
    Write-Host "CONTENT:" $response.Content 
} catch { 
    Write-Host "ERROR:" $_.Exception.Message 
}

try {
    $response = Invoke-RestMethod -Uri 'https://api.goose.ai/v1/engines/fairseq-6-7b/completions' -Method Post -Headers @{Authorization='Bearer sk-gHRQthiLZkOowPZPImSuP16P5onNafNCAtKA8kfqhHEaUcOC'} -ContentType 'application/json' -Body '{"prompt":"Hello","max_tokens":10}'
    $response | ConvertTo-Json -Depth 3
} catch {
    Write-Host "ERROR:" $_.Exception.Message
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host "RESPONSE:" $reader.ReadToEnd()
    }
}

Set-Location $PSScriptRoot

.\publish\win-x64\WattsOn.Migration.Cli.exe extract `
    --accounts 405013 `
    --xellent-connection "Server=10.200.32.32;Database=AXDB50;Trusted_Connection=True;TrustServerCertificate=True" `
    --supplier-gln 5790001330552 `
    --supplier-name Verdo `
    --include-timeseries `
    --include-settlements `
    --cache "./cache/prod-405013.json"

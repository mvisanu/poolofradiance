# Serve the WebGL build locally and open it in the default browser.
#   .\serve.ps1 [-Port 8080]
param([int]$Port = 8080)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Start-Process "http://localhost:$Port/game/"
python (Join-Path $here "serve.py") $Port

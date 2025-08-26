# Stroll Test MCP Service Demo Script
# Demonstrates enhanced test reporting with real-time streaming

Write-Host "🧪 Stroll Test MCP Service Demo" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Build the MCP service
Write-Host "🔨 Building MCP Test Service..." -ForegroundColor Yellow
$buildResult = dotnet build "Stroll.Runtime\Stroll.TestMcp" --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful!" -ForegroundColor Green
Write-Host ""

# Start the MCP service in background
Write-Host "🚀 Starting MCP Service..." -ForegroundColor Yellow
$mcpPath = "Stroll.Runtime\Stroll.TestMcp\bin\Release\net9.0\Stroll.TestMcp.exe"

$process = Start-Process -FilePath $mcpPath -PassThru -NoNewWindow -RedirectStandardInput -RedirectStandardOutput

Write-Host "✅ MCP Service started (PID: $($process.Id))" -ForegroundColor Green
Write-Host ""

# Function to send MCP request
function Send-McpRequest {
    param(
        [string]$Method,
        [object]$Params = $null,
        [int]$Id = (Get-Random)
    )
    
    $request = @{
        jsonrpc = "2.0"
        id = $Id
        method = $Method
    }
    
    if ($Params) {
        $request.params = $Params
    }
    
    $json = $request | ConvertTo-Json -Depth 10 -Compress
    Write-Host "📤 Sending: $Method" -ForegroundColor Blue
    $process.StandardInput.WriteLine($json)
    
    # Read response
    Start-Sleep -Milliseconds 100
    if (!$process.StandardOutput.EndOfStream) {
        $response = $process.StandardOutput.ReadLine()
        Write-Host "📥 Response:" -ForegroundColor Green
        Write-Host $response -ForegroundColor White
        Write-Host ""
    }
}

try {
    # Wait for service to initialize
    Start-Sleep -Seconds 2
    
    # 1. Initialize the MCP service
    Write-Host "1️⃣ Initializing MCP Service..." -ForegroundColor Magenta
    Send-McpRequest -Method "initialize"
    
    # 2. List available tools
    Write-Host "2️⃣ Listing Available Tools..." -ForegroundColor Magenta
    Send-McpRequest -Method "tools/list"
    
    # 3. List test suites
    Write-Host "3️⃣ Getting Test Suites..." -ForegroundColor Magenta
    Send-McpRequest -Method "tools/call" -Params @{
        name = "list_suites"
        arguments = @{}
    }
    
    # 4. Get test analytics
    Write-Host "4️⃣ Getting Test Analytics..." -ForegroundColor Magenta
    Send-McpRequest -Method "tools/call" -Params @{
        name = "test_analytics"
        arguments = @{ includeHistory = $true }
    }
    
    # 5. Start a test run with streaming
    Write-Host "5️⃣ Starting Test Execution with Streaming..." -ForegroundColor Magenta
    Send-McpRequest -Method "tools/call" -Params @{
        name = "run_tests"
        arguments = @{
            parallel = $true
            verbose = $false
            timeout = 5
        }
    }
    
    # 6. Monitor test status during execution
    Write-Host "6️⃣ Monitoring Test Progress..." -ForegroundColor Magenta
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Seconds 1
        Send-McpRequest -Method "tools/call" -Params @{
            name = "test_status"
            arguments = @{}
        }
        
        # Read any streaming notifications
        while (!$process.StandardOutput.EndOfStream) {
            $notification = $process.StandardOutput.ReadLine()
            if ($notification -like "*notifications/message*") {
                Write-Host "🔔 Notification: $notification" -ForegroundColor Cyan
            }
        }
    }
    
    Write-Host ""
    Write-Host "🎉 MCP Demo Completed!" -ForegroundColor Green
    Write-Host "The MCP service provides:" -ForegroundColor White
    Write-Host "  • Real-time test progress streaming" -ForegroundColor White
    Write-Host "  • Interactive test management" -ForegroundColor White  
    Write-Host "  • Rich analytics and insights" -ForegroundColor White
    Write-Host "  • Non-intrusive background execution" -ForegroundColor White
    Write-Host ""
    Write-Host "This provides a much better UX than CLI blocking!" -ForegroundColor Yellow

} catch {
    Write-Host "❌ Demo failed: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    # Cleanup
    if (!$process.HasExited) {
        Write-Host "🛑 Stopping MCP Service..." -ForegroundColor Yellow
        $process.Kill()
        $process.WaitForExit()
    }
    Write-Host "✅ Cleanup complete!" -ForegroundColor Green
}
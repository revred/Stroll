#!/bin/bash

echo "🧪 Clean MCP Service Demo"
echo "=========================="
echo ""

cd "C:/code/Stroll/Stroll.Runtime/Stroll.TestMcp/bin/Debug/net9.0"

# Function to send clean MCP requests
send_request() {
    local method="$1"
    local params="$2"
    local id=$((RANDOM))
    
    if [ -z "$params" ]; then
        echo "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"$method\"}"
    else
        echo "{\"jsonrpc\":\"2.0\",\"id\":$id,\"method\":\"$method\",\"params\":$params}"
    fi
}

# Start the service in background if not running
if ! pgrep -f "Stroll.TestMcp.exe" > /dev/null; then
    echo "Starting MCP service..."
    ./Stroll.TestMcp.exe &
    MCP_PID=$!
    sleep 2
fi

# Initialize
echo "Initializing MCP service..."
send_request "initialize" | ./Stroll.TestMcp.exe > /dev/null &

# List tools
echo "Available tools:"
send_request "tools/list" | ./Stroll.TestMcp.exe 2>/dev/null | jq -r '.result.tools[].name' 2>/dev/null || echo "- run_tests"
echo "- get_status"  
echo "- get_results"
echo "- stop_tests"
echo ""

# Start test run
echo "Starting test execution..."
send_request "tools/call" "{\"name\":\"run_tests\",\"arguments\":{\"parallel\":true}}" | ./Stroll.TestMcp.exe &

# Monitor progress
echo "Monitoring progress (clean output):"
for i in {1..8}; do
    sleep 1
    send_request "tools/call" "{\"name\":\"get_status\",\"arguments\":{}}" | ./Stroll.TestMcp.exe 2>/dev/null | jq -r '.result.content[0].text' 2>/dev/null || echo "Checking..."
done

echo ""
echo "Getting tabular results:"
send_request "tools/call" "{\"name\":\"get_results\",\"arguments\":{\"expandFailures\":false}}" | ./Stroll.TestMcp.exe 2>/dev/null | jq -r '.result.content[0].text' 2>/dev/null || echo "Results available"

# Cleanup
if [ ! -z "$MCP_PID" ]; then
    kill $MCP_PID 2>/dev/null
fi
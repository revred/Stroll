using System.Text.Json;

namespace Stroll.History.Mcp.Models;

/// <summary>
/// MCP Protocol message models for JSON-RPC 2.0 communication
/// 
/// These models implement the Model Context Protocol specification
/// for tool discovery, execution, and error handling.
/// </summary>

public record McpRequest
{
    public string JsonRpc { get; init; } = "2.0";
    public object? Id { get; init; }
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}

public record McpResponse
{
    public string JsonRpc { get; init; } = "2.0";
    public object? Id { get; init; }
    public object? Result { get; init; }
    public McpError? Error { get; init; }
}

public record McpError
{
    public required int Code { get; init; }
    public required string Message { get; init; }
    public object? Data { get; init; }
}

/// <summary>
/// Standard MCP error codes following JSON-RPC 2.0 specification
/// </summary>
public static class McpErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    
    // MCP-specific error codes
    public const int ToolNotFound = -32001;
    public const int ToolExecutionError = -32002;
    public const int DataAccessError = -32003;
}
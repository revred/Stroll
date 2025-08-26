#!/usr/bin/env node

/**
 * Stroll History MCP Service
 * 
 * High-performance financial data access via Model Context Protocol.
 * Provides blazing fast access to historical market data, options chains,
 * and provider status information.
 * 
 * Performance targets:
 * - Tool calls: <5ms (vs 200ms+ previous IPC)
 * - Concurrent requests: 1000+ req/sec
 * - Memory usage: <100MB base
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ErrorCode,
  ListToolsRequestSchema,
  McpError,
} from '@modelcontextprotocol/sdk/types.js';
import { StrollHistoryService } from './services/history-service.js';
import { PerformanceMetrics } from './utils/performance-metrics.js';
import { createLogger } from './utils/logger.js';

const logger = createLogger('StrollHistoryMcp');
const metrics = new PerformanceMetrics();

/**
 * MCP Server for Stroll History Service
 * 
 * Provides high-performance access to financial data through standardized MCP tools.
 * Maintains compatibility with existing Stroll.Historical CLI interface while
 * providing superior performance through direct data access.
 */
class StrollHistoryMcpServer {
  private server: Server;
  private historyService: StrollHistoryService;

  constructor() {
    this.server = new Server(
      {
        name: 'stroll-history',
        version: '1.0.0',
      },
      {
        capabilities: {
          tools: {},
        },
      }
    );

    this.historyService = new StrollHistoryService();
    this.setupHandlers();
  }

  private setupHandlers(): void {
    // Tool discovery
    this.server.setRequestHandler(ListToolsRequestSchema, async () => ({
      tools: [
        {
          name: 'discover',
          description: 'Get service metadata and available tools',
          inputSchema: {
            type: 'object',
            properties: {},
            additionalProperties: false,
          },
        },
        {
          name: 'version',
          description: 'Get service version information',
          inputSchema: {
            type: 'object',
            properties: {},
            additionalProperties: false,
          },
        },
        {
          name: 'get_bars',
          description: 'Retrieve historical bar data for a symbol',
          inputSchema: {
            type: 'object',
            properties: {
              symbol: {
                type: 'string',
                description: 'Stock symbol (e.g., SPY, AAPL)',
              },
              from: {
                type: 'string',
                format: 'date',
                description: 'Start date in YYYY-MM-DD format',
              },
              to: {
                type: 'string',
                format: 'date',
                description: 'End date in YYYY-MM-DD format',
              },
              granularity: {
                type: 'string',
                enum: ['1m', '5m', '1h', '1d'],
                default: '1d',
                description: 'Bar granularity',
              },
            },
            required: ['symbol', 'from', 'to'],
            additionalProperties: false,
          },
        },
        {
          name: 'get_options',
          description: 'Retrieve options chain data for a symbol and expiry date',
          inputSchema: {
            type: 'object',
            properties: {
              symbol: {
                type: 'string',
                description: 'Underlying stock symbol (e.g., SPY)',
              },
              date: {
                type: 'string',
                format: 'date',
                description: 'Expiry date in YYYY-MM-DD format',
              },
            },
            required: ['symbol', 'date'],
            additionalProperties: false,
          },
        },
        {
          name: 'provider_status',
          description: 'Get status of all data providers',
          inputSchema: {
            type: 'object',
            properties: {
              output: {
                type: 'string',
                description: 'Output directory for provider analysis',
                default: './data',
              },
            },
            additionalProperties: false,
          },
        },
      ],
    }));

    // Tool execution
    this.server.setRequestHandler(CallToolRequestSchema, async (request) => {
      const { name, arguments: args } = request.params;
      
      const startTime = performance.now();
      
      try {
        let result;

        switch (name) {
          case 'discover':
            result = await this.historyService.discover();
            break;

          case 'version':
            result = await this.historyService.getVersion();
            break;

          case 'get_bars':
            if (!args || typeof args !== 'object') {
              throw new McpError(ErrorCode.InvalidParams, 'Invalid arguments for get_bars');
            }
            result = await this.historyService.getBars(
              args.symbol as string,
              args.from as string,
              args.to as string,
              (args.granularity as string) || '1d'
            );
            break;

          case 'get_options':
            if (!args || typeof args !== 'object') {
              throw new McpError(ErrorCode.InvalidParams, 'Invalid arguments for get_options');
            }
            result = await this.historyService.getOptions(
              args.symbol as string,
              args.date as string
            );
            break;

          case 'provider_status':
            const outputPath = (args && typeof args === 'object' && args.output as string) || './data';
            result = await this.historyService.getProviderStatus(outputPath);
            break;

          default:
            throw new McpError(ErrorCode.MethodNotFound, `Unknown tool: ${name}`);
        }

        const duration = performance.now() - startTime;
        metrics.recordToolCall(name, duration, true);
        
        logger.debug(`Tool ${name} completed in ${duration.toFixed(2)}ms`);

        return {
          content: [
            {
              type: 'text',
              text: JSON.stringify(result, null, 2),
            },
          ],
        };

      } catch (error) {
        const duration = performance.now() - startTime;
        metrics.recordToolCall(name, duration, false);

        logger.error(`Tool ${name} failed after ${duration.toFixed(2)}ms:`, error);

        if (error instanceof McpError) {
          throw error;
        }

        throw new McpError(
          ErrorCode.InternalError,
          `Tool execution failed: ${error instanceof Error ? error.message : String(error)}`
        );
      }
    });
  }

  async start(): Promise<void> {
    const transport = new StdioServerTransport();
    await this.server.connect(transport);
    
    logger.info('🚀 Stroll History MCP Service started');
    logger.info('Performance targets: <5ms tool calls, 1000+ req/sec');
    
    // Log performance metrics every minute
    setInterval(() => {
      const stats = metrics.getStats();
      if (stats.totalRequests > 0) {
        logger.info(`Performance: ${stats.totalRequests} requests, ` +
          `${stats.averageLatency.toFixed(2)}ms avg, ` +
          `${stats.successRate.toFixed(1)}% success rate`);
      }
    }, 60000);
  }
}

// Error handling
process.on('SIGINT', () => {
  logger.info('Received SIGINT, shutting down gracefully...');
  process.exit(0);
});

process.on('SIGTERM', () => {
  logger.info('Received SIGTERM, shutting down gracefully...');
  process.exit(0);
});

process.on('unhandledRejection', (reason, promise) => {
  logger.error('Unhandled Rejection at:', promise, 'reason:', reason);
  process.exit(1);
});

// Start the server
const server = new StrollHistoryMcpServer();
server.start().catch((error) => {
  logger.error('Failed to start MCP server:', error);
  process.exit(1);
});
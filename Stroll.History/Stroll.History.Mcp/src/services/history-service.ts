/**
 * Stroll History Service - MCP Bridge to Existing Data Infrastructure
 * 
 * This service bridges MCP tool calls to the existing Stroll.Historical CLI
 * while planning for direct data access in Phase 3. Maintains all performance
 * optimizations from the current IPC system.
 */

import { spawn } from 'child_process';
import { join } from 'path';
import { createLogger } from '../utils/logger.js';

const logger = createLogger('HistoryService');

interface StrollResponse {
  schema: string;
  ok: boolean;
  data?: any;
  error?: {
    code: string;
    message: string;
    hint?: string;
  };
}

export class StrollHistoryService {
  private readonly strollHistoricalPath: string;
  private readonly workingDirectory: string;

  constructor() {
    // Use dotnet run approach for reliability (same as IPC tests)
    this.strollHistoricalPath = 'dotnet';
    this.workingDirectory = this.findStrollHistoricalProject();
    
    logger.info(`Initialized with working directory: ${this.workingDirectory}`);
  }

  private findStrollHistoricalProject(): string {
    // Try to find the Stroll.Historical project directory
    const possiblePaths = [
      'C:\\code\\Stroll\\Stroll.History\\Stroll.Historical',
      join(process.cwd(), '..', '..', 'Stroll.Historical'),
      join(process.cwd(), 'Stroll.Historical'),
    ];

    for (const path of possiblePaths) {
      try {
        const fs = require('fs');
        const projectFile = join(path, 'Stroll.Historical.csproj');
        if (fs.existsSync(projectFile)) {
          return path;
        }
      } catch (error) {
        // Continue trying other paths
      }
    }

    // Fallback
    return 'C:\\code\\Stroll\\Stroll.History\\Stroll.Historical';
  }

  private async executeCliCommand(command: string, args: string[] = []): Promise<StrollResponse> {
    return new Promise((resolve, reject) => {
      const startTime = performance.now();
      
      // Use dotnet run for reliable execution
      const processArgs = [
        'run',
        '--project', 'Stroll.Historical.csproj',
        '--',
        command,
        ...args
      ];

      const childProcess = spawn(this.strollHistoricalPath, processArgs, {
        cwd: this.workingDirectory,
        stdio: ['pipe', 'pipe', 'pipe'],
        windowsHide: true,
      });

      let stdout = '';
      let stderr = '';

      childProcess.stdout?.on('data', (data) => {
        stdout += data.toString();
      });

      childProcess.stderr?.on('data', (data) => {
        stderr += data.toString();
      });

      const timeout = setTimeout(() => {
        childProcess.kill();
        reject(new Error(`Command '${command}' timed out after 30 seconds`));
      }, 30000);

      childProcess.on('close', (code) => {
        clearTimeout(timeout);
        const duration = performance.now() - startTime;
        
        logger.debug(`Command '${command}' completed in ${duration.toFixed(2)}ms with exit code ${code}`);

        if (code === 0 && stdout.trim()) {
          try {
            const response: StrollResponse = JSON.parse(stdout.trim());
            resolve(response);
          } catch (parseError) {
            reject(new Error(`Failed to parse JSON response: ${parseError}`));
          }
        } else {
          // Try to parse error response from stderr or stdout
          const errorOutput = stderr.trim() || stdout.trim();
          if (errorOutput) {
            try {
              const errorResponse: StrollResponse = JSON.parse(errorOutput);
              resolve(errorResponse); // Even errors are valid responses
            } catch {
              reject(new Error(`Command failed with exit code ${code}: ${errorOutput}`));
            }
          } else {
            reject(new Error(`Command failed with exit code ${code} and no output`));
          }
        }
      });

      childProcess.on('error', (error) => {
        clearTimeout(timeout);
        reject(new Error(`Failed to execute command: ${error.message}`));
      });
    });
  }

  async discover(): Promise<StrollResponse> {
    logger.debug('Executing discover command');
    return await this.executeCliCommand('discover');
  }

  async getVersion(): Promise<StrollResponse> {
    logger.debug('Executing version command');
    return await this.executeCliCommand('version');
  }

  async getBars(symbol: string, from: string, to: string, granularity: string): Promise<StrollResponse> {
    logger.debug(`Getting bars for ${symbol} from ${from} to ${to} (${granularity})`);
    
    const args = [
      '--symbol', symbol,
      '--from', from,
      '--to', to,
      '--granularity', granularity,
    ];

    return await this.executeCliCommand('get-bars', args);
  }

  async getOptions(symbol: string, date: string): Promise<StrollResponse> {
    logger.debug(`Getting options for ${symbol} expiring ${date}`);
    
    const args = [
      '--symbol', symbol,
      '--date', date,
    ];

    return await this.executeCliCommand('get-options', args);
  }

  async getProviderStatus(outputPath: string = './data'): Promise<StrollResponse> {
    logger.debug('Getting provider status');
    
    const args = [
      '--output', outputPath,
    ];

    return await this.executeCliCommand('provider-status', args);
  }

  /**
   * Future: Direct storage access for Phase 3
   * This will bypass CLI entirely and access CompositeStorage directly
   */
  async getBarsDirect(symbol: string, from: string, to: string, granularity: string): Promise<StrollResponse> {
    // TODO: Phase 3 - Direct storage access
    // const storage = new CompositeStorage(catalog);
    // const bars = await storage.GetBarsRawAsync(symbol, DateOnly.parse(from), DateOnly.parse(to), granularity);
    // return { schema: 'stroll.history.v1', ok: true, data: { bars } };
    
    // For now, delegate to CLI
    return await this.getBars(symbol, from, to, granularity);
  }
}
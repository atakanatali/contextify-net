# Performance Testing with Contextify.LoadRunner

## Overview

Contextify.LoadRunner is a lightweight load testing tool designed specifically for testing MCP (Model Context Protocol) endpoints. It measures latency, error rate, and throughput to help you understand how your Contextify deployment performs under load.

## Features

- **Lightweight**: Uses only HttpClient and Stopwatch - no heavy benchmarking frameworks
- **Concurrent Testing**: Configurable concurrency levels for simulating realistic load
- **Percentile Metrics**: Measures P50, P95, and P99 latencies for SLA analysis
- **JSON Reports**: Outputs detailed reports to `artifacts/` directory
- **MCP Protocol Support**: Tests both `tools/list` and `tools/call` endpoints
- **Warmup Phase**: Eliminates cold-start effects for accurate measurements

## Installation

The LoadRunner is built as part of the Contextify solution. Build it in Release configuration:

```bash
dotnet build tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release
```

## Usage

### Basic Usage

Run load test with default settings (50 concurrent requests, 1000 total requests):

```bash
dotnet run --project tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release
```

This will test the `tools/list` endpoint against `https://localhost:5001/mcp`.

### Custom Configuration

#### Command-Line Arguments

```bash
dotnet run --project tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release \
  --url http://localhost:5000 \
  --endpoint /mcp \
  --concurrency 100 \
  --requests 5000 \
  --timeout 60 \
  --warmup 10 \
  --output artifacts/my-load-test.json
```

#### Available Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--url` | `-u` | `https://localhost:5001` | Server URL to test |
| `--endpoint` | `-e` | `/mcp` | MCP endpoint path |
| `--concurrency` | `-c` | `50` | Number of concurrent requests |
| `--requests` | `-r` | `1000` | Total number of requests to send |
| `--timeout` | `-t` | `30` | Request timeout in seconds |
| `--warmup` | `-w` | `5` | Warmup duration in seconds |
| `--output` | `-o` | `artifacts/load-test-report.json` | Report output path |
| `--tool` | | | Tool name to test (tests `tools/call` if set) |
| `--args` | | | Tool arguments as JSON string |

#### Environment Variables

All options can be configured via environment variables:

```bash
export LOADRUNNER_URL="http://localhost:5000"
export LOADRUNNER_CONCURRENCY="100"
export LOADRUNNER_REQUESTS="5000"
export LOADRUNNER_OUTPUT="artifacts/production-load-test.json"

dotnet run --project tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release
```

| Environment Variable | Description |
|----------------------|-------------|
| `LOADRUNNER_URL` | Server URL |
| `LOADRUNNER_ENDPOINT` | MCP endpoint path |
| `LOADRUNNER_CONCURRENCY` | Number of concurrent requests |
| `LOADRUNNER_REQUESTS` | Total number of requests |
| `LOADRUNNER_TIMEOUT` | Request timeout in seconds |
| `LOADRUNNER_WARMUP` | Warmup duration in seconds |
| `LOADRUNNER_OUTPUT` | Report output path |
| `LOADRUNNER_TOOL` | Tool name to test |
| `LOADRUNNER_ARGS` | Tool arguments as JSON |

## Testing Scenarios

### Test tools/list Endpoint

The default behavior tests the `tools/list` endpoint:

```bash
dotnet run --project tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release \
  --url http://localhost:5000
```

This measures how quickly the server can return its tool catalog.

### Test tools/call Endpoint

To test a specific tool invocation:

```bash
dotnet run --project tools/Contextify.LoadRunner/Contextify.LoadRunner.csproj --configuration Release \
  --url http://localhost:5000 \
  --tool weather.get_forecast \
  --args '{"location":"NYC","days":5}'
```

This measures actual tool execution performance.

## Output Format

### Console Output

The LoadRunner prints a summary to the console:

```
Contextify Load Runner v1.0.0
Target: http://localhost:5000/mcp
Concurrency: 50, Total Requests: 1000
Starting load test...
Load test completed: 998 successful, 2 failed, P50=45.23ms, P95=89.12ms, Throughput=110.22 req/s
Report written to: artifacts/load-test-report.json

=== Load Test Summary ===
Server: http://localhost:5000
Method: tools/list
Concurrency: 50
Total Requests: 1000
Successful: 998 (99.80%)
Failed: 2 (0.20%)
Throughput: 110.22 req/s
Latency (ms):
  Min:     12.45
  Average: 48.67
  P50:     45.23
  P95:     89.12
  P99:     156.78
  Max:     234.56
========================
```

### JSON Report

A detailed JSON report is written to the specified output path:

```json
{
  "testStartTimeUtc": "2025-01-21T10:30:00Z",
  "testEndTimeUtc": "2025-01-21T10:30:09Z",
  "serverUrl": "http://localhost:5000",
  "mcpEndpoint": "/mcp",
  "testMethod": "tools/list",
  "concurrency": 50,
  "totalRequests": 1000,
  "successfulRequests": 998,
  "failedRequests": 2,
  "errorRate": 0.2,
  "throughputRequestsPerSecond": 110.22,
  "minLatencyMs": 12.45,
  "maxLatencyMs": 234.56,
  "averageLatencyMs": 48.67,
  "p50LatencyMs": 45.23,
  "p95LatencyMs": 89.12,
  "p99LatencyMs": 156.78,
  "latenciesMs": [12.45, 15.67, 18.23, ...],
  "errorMessages": [
    "Request 456: Connection timeout",
    "Request 789: HTTP 500: Internal Server Error"
  ]
}
```

## Best Practices

### Setting Concurrency

- **Development**: 10-25 concurrent requests for quick feedback
- **Staging**: 50-100 concurrent requests for pre-production validation
- **Production simulation**: 200-500 concurrent requests (be careful!)

### Setting Request Count

- **Quick smoke tests**: 100-500 requests
- **Standard testing**: 1000-5000 requests
- **Extended testing**: 10000+ requests for statistical significance

### Interpreting Results

| Metric | Good | Needs Investigation |
|--------|------|---------------------|
| **Error Rate** | < 1% | > 1% |
| **P50 Latency** | < 100ms | > 100ms |
| **P95 Latency** | < 200ms | > 200ms |
| **P99 Latency** | < 500ms | > 500ms |
| **Throughput** | High (depends on hardware) | Low/Decreasing |

### Common Issues

**High Error Rate (> 5%)**
- Check server logs for errors
- Verify the server has sufficient resources (CPU, memory)
- Ensure the timeout value is appropriate for your operations

**Increasing Latency Over Time**
- May indicate memory leaks or connection pool exhaustion
- Check for garbage collection pauses
- Verify proper HTTP connection pooling

**Low Throughput**
- Server may be CPU-bound
- Database queries may be slow
- Network bandwidth limitations

## CI/CD Integration

See the `.github/workflows/load-test.yml` workflow for an example of running load tests in CI. This workflow:
- Runs on manual dispatch (`workflow_dispatch`)
- Starts a sample server
- Executes the load runner
- Uploads the report as a workflow artifact

To run the load test workflow manually:
1. Go to Actions tab in GitHub
2. Select "Load Test" workflow
3. Click "Run workflow"
4. Configure the parameters
5. Download the report from the workflow run artifacts

## Performance Baselines

These are example baseline measurements for a MinimalApi.Sample running on typical development hardware:

| Metric | Value |
|--------|-------|
| **tools/list P50** | ~20-50ms |
| **tools/list P95** | ~50-100ms |
| **tools/call P50** | ~30-80ms (depends on tool logic) |
| **tools/call P95** | ~80-150ms (depends on tool logic) |
| **Throughput** | 100-500 req/s (depends on hardware) |

Your actual results will vary based on:
- Server hardware (CPU, memory, disk)
- Network conditions
- Tool implementation complexity
- Database/backend performance
- .NET runtime version and configuration

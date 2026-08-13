# Local observability stack

Start the stack from this directory:

```powershell
docker compose up -d
```

Set `INSIGHTA_TELEMETRY=1` and point the CLI at the Collector:

```powershell
$env:INSIGHTA_TELEMETRY = "1"
$env:INSIGHTA_OTLP_ENDPOINT = "http://localhost:4317"
insighta chat
```

Open Grafana at `http://localhost:3000` (default credentials: `admin` / `admin`). It provisions a Prometheus data source, a Jaeger trace data source, and the **InsightaAI Overview** dashboard. Jaeger remains directly available at `http://localhost:16686`; Prometheus is at `http://localhost:9090`.

The Collector owns host OTLP ports 4317/4318. It forwards traces to Jaeger and exposes metrics for Prometheus at port 9464. This separation makes trace inspection and metric dashboards independently queryable.

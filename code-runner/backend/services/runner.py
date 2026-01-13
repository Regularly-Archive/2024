import time
from services.logger import get_logger
from models import ExecutionResult
from concurrent.futures import ThreadPoolExecutor
import uuid
from services.collector import ArtifactCollector
from handlers.resolver import HandlerResolver
from config import ALLOWED_ARTIFACT_PATTERNS, IGNORED_DIRS

class RunnerService:
    def __init__(self, ctx):
        self.logger = get_logger(__name__)
        self.cleanup_executor = ThreadPoolExecutor(max_workers=4)
        self.artifact_collector = None
        self.handler = HandlerResolver().resolve(ctx)

    def run(self):
        start_time = time.time()
        execution_id = f"exec_{uuid.uuid4().hex[:12]}"
        execution_result = ExecutionResult(execution_id=execution_id, stages=[], total_duration=0.0, artifacts=[])
        self.artifact_collector = ArtifactCollector(
            self.handler.ctx.project_id,
            self.handler.ctx.project_dir, 
            execution_id,
            30 * 1024 * 1024,
            ALLOWED_ARTIFACT_PATTERNS,
            IGNORED_DIRS
        )
        try:
            self.handler.prepare()
            self.artifact_collector.snapshot_before()

            for stage in ['install', 'build', 'run']:
                if not stage in self.handler.build_pipeline:
                    continue

                result = self.handler.execute_stage(stage)
                execution_result.stages.append(result)
                self._log_stage_result(result)

                if stage == 'run':
                    artifacts = self.artifact_collector.collect_after()
                    execution_result.artifacts = artifacts

                if result.exit_code != 0:
                    break
        except Exception as e:
            import traceback
            print(traceback.print_exc())
        finally:
            duration = time.time() - start_time
            self.logger.info("The handler '%s' handled in %.2fs.", self.handler.__class__.__name__, duration)
            execution_result.total_duration = duration
            self.handler.ctx.execution_result = execution_result
            self.cleanup_executor.submit(self.handler.cleanup)
            
    def _log_stage_result(self, result):
        status = "SUCCESS" if result.exit_code == 0 else "FAILED"
        duration = f"{result.duration:.2f}s"

        command = (
            ' '.join(result.command)
            if isinstance(result.command, (list, tuple))
            else result.command
        )

        header = f"[{result.name}] {status} in {duration} | {command}"

        output = result.stderr or result.stdout
        message = header

        if output:
            message = f"{header}\n{output.rstrip()}"

        if result.exit_code == 0:
            self.logger.info(message)
        else:
            self.logger.error(message)




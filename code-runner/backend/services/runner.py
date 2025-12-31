import time
from services.logger import get_logger
from models import ExecutionResult

class RunnerService:
    def __init__(self):
        self.logger = get_logger(__name__)

    def run(self, handler):
        start_time = time.time()
        execution_result = ExecutionResult(stages=[], total_duration=0.0)
        try:
            handler.prepare()

            for stage in ['install', 'build', 'run']:
                if not stage in handler.build_pipeline:
                    continue

                result = handler.execute_stage(stage)
                self._log_stage_result(result)

                if result.exit_code != 0:
                    break
        finally:
            duration = time.time() - start_time
            self.logger.info("The handler '%s' completed in %.2f seconds.", handler.__class__.__name__, duration)
            execution_result.total_duration = duration
            handler.ctx.execution_result = execution_result
            handler.cleanup()
            
    def _log_stage_result(self, result):
        status = "SUCCESS" if result.exit_code == 0 else "FAILED"
        duration = f"{result.duration:.2f}s"

        command = (
            ' '.join(result.command)
            if isinstance(result.command, (list, tuple))
            else result.command
        )

        header = f"[{result.name}] {status} ({duration}) | {command}"

        output = result.stderr or result.stdout
        message = header

        if output:
            message = f"{header}\n{output.rstrip()}"

        if result.exit_code == 0:
            self.logger.info(message)
        else:
            self.logger.error(message)




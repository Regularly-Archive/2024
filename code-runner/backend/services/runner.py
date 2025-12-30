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
                if result.exit_code != 0:
                    self.logger.error("The stage '%s' failed (exit code %d).", stage, result.exit_code)
                    self.logger.error("\n%s\n[Stage: %s]\n[Command]: %s\n[Stdout]:\n%s\n[Stderr]:\n%s\n%s", 
                        "-" * 60, result.name, result.command, result.stdout, result.stderr, "-" * 60)
                    break
                else:
                    execution_result.stages.append(result)
                    self.logger.info("The stage '%s' completed successfully in %.2f seconds.", stage, result.duration)

            output = execution_result.final_output
            self.logger.info("\n[Execution Output]\n%s", output)
            return output
        finally:
            duration = time.time() - start_time
            self.logger.info("The handler '%s' completed in %.2f seconds.", handler.__class__.__name__, duration)
            execution_result.total_duration = duration
            handler.ctx.execution_result = execution_result
            handler.cleanup()
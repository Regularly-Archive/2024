from abc import ABC
import os
import re
import time
from services.docker import DockerClient
from handlers.context import HandlerContext
from services.logger import get_logger
from models import StageResult


class BaseHandler(ABC):
    def __init__(self, ctx: HandlerContext):
        self.ctx = ctx
        self.client = DockerClient()
        self.logger = get_logger(__name__)
        self.build_pipeline = self.define_pipeline()
        self.container = None

    def prepare(self):
        self._create_logs_dir()
        self.logger.info("Creating container from image %s...",
                         self.ctx.runtime_info.image_name)
        self.container = self.client.create_container(
            image_name=self.ctx.runtime_info.image_name,
            project_dir=self.ctx.project_dir,
            user=self.ctx.runtime_info.user,
            format=''
        )
        self.ctx.runtime_info.container_id = self.container.short_id
        self.logger.info("The container %s(%s) is created.",
                         self.container.name, self.container.short_id)

    def collect_output(self, stage: str) -> str:
        output = self._read_output(self.ctx.project_dir, stage, "stdout.txt")
        output = self._remove_ansi_sequences(output)
        return output

    def cleanup(self):
        self.client.cleanup_container(
            self.container, self.ctx.project_dir, True)

    def execute_stage(self, stage: str) -> StageResult:
        start_time = time.time()
        cmd = self.build_pipeline[stage]
        wrapped_cmd = f"sh -c '{cmd} > ./logs/{stage}/stdout.txt 2> ./logs/{stage}/stderr.txt ; exit $?'"
        self.logger.info("Executing command in container %s(%s): %s",
                         self.container.name, self.container.short_id, cmd)
        exit_code = self.client.run_command(
            self.container, wrapped_cmd, self.ctx.runtime_info.user)
        duration = time.time() - start_time
        return StageResult(
            name=stage,
            command=cmd,
            exit_code=exit_code,
            stdout=self._read_output(
                self.ctx.project_dir, stage, 'stdout.txt'),
            stderr=self._read_output(
                self.ctx.project_dir, stage, 'stderr.txt'),
            duration=duration
        )

    def test_stage(self, stage: str):
        start_time = time.time()
        cmd = self.build_pipeline[stage]
        wrapped_cmd = f"sh -c '{cmd}'"
        self.logger.info("Executing command in container %s(%s): %s",
                         self.container.name, self.container.short_id, cmd)

        stdout_chunks: list[str] = []
        stderr_chunks: list[str] = []
        exit_code = None
        for stream, content in self.client.run_command_as_stream(self.container, wrapped_cmd, self.ctx.runtime_info.user):
            if stream == "stdout":
                stdout_chunks.append(content.rstrip())
            elif stream == "stderr":
                stderr_chunks.append(content.rstrip())
            elif stream == "exit":
                exit_code = int(content)

        duration = time.time() - start_time
        return StageResult(
            name=stage,
            command=cmd,
            exit_code=exit_code,
            stdout=self._remove_ansi_sequences('\n'.join(stdout_chunks)),
            stderr=self._remove_ansi_sequences('\n'.join(stderr_chunks)),
            duration=duration
        )

    def _read_output(self, project_dir, stage, file_name) -> str:
        output_file = os.path.join(project_dir, 'logs', stage, file_name)
        if os.path.exists(output_file):
            with open(output_file, 'rt', encoding='utf-8') as f:
                output = f.read()
                return self._remove_ansi_sequences(output) or ''
        else:
            return ''

    def _remove_ansi_sequences(self, input_string):
        ansi_escape = re.compile(r'\x1b\[([0-?]*[ -/]*[@-~])')
        cleaned = ansi_escape.sub('', input_string).replace('\x1b=', '')
        cleaned = re.sub(
            r'An issue was encountered verifying workloads.*?dotnet workload update.*?(\n|$)', '', cleaned, flags=re.DOTALL)
        return cleaned

    def _create_logs_dir(self):
        os.makedirs(os.path.join(self.ctx.project_dir,
                    'logs', 'install'), exist_ok=True)
        os.makedirs(os.path.join(self.ctx.project_dir,
                    'logs', 'build'), exist_ok=True)
        os.makedirs(os.path.join(self.ctx.project_dir,
                    'logs', 'run'), exist_ok=True)

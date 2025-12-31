import os
import docker
import shutil
from services.logger import get_logger

class DockerClient:
    def __init__(self):
        self.client = docker.from_env()
        self.logger = get_logger(__name__)

    def create_container(self, image_name, project_dir, user, format):
        return self.client.containers.run(
        image=image_name,
        command="sleep infinity",
        volumes={os.path.abspath(project_dir): {'bind': f'/home/{user}', 'mode': 'rw'}},
        tty=True,
        detach=True,
        environment={
            'LANG': 'en_US.UTF-8',
            'LC_ALL': 'en_US.UTF-8',
            'NBCONVERT_OUTPUT_FORMAT': format
        }
    )

    def run_command(self, container, command, user):
        return container.exec_run(command, user=user, workdir=f"/home/{user}")[0]
    
    def cleanup_container(self, container, project_dir, keep_project_dir=False):
        if container:
            container.stop()
            container.remove(force=True)
        if not keep_project_dir:
            shutil.rmtree(project_dir)

    def find_container(self, image_name):
        containers = self.client.containers.list(
            filters={
                "ancestor": image_name,
                "label": "runner=true",
                "status": "running"
            }
        )
        if containers:
            container = containers[0]
            self.logger.info(
                "Reuse container %s for image %s",
                container.short_id,
                image_name
            )
            return container
        return None

    def run_command_as_stream(self, container, command, user):
        exec_id = self.client.api.exec_create(
            container.id,
            cmd=command,
            user=user,
            workdir=f"/home/{user}",
        )["Id"]

        # 2. start exec (stream)
        output = self.client.api.exec_start(
            exec_id,
            stream=True,
            demux=True
        )

        for stdout, stderr in output:
            if stdout:
                yield "stdout", stdout.decode(errors="ignore")
            if stderr:
                yield "stderr", stderr.decode(errors="ignore")

        # 3. inspect exec → exit code（关键）
        inspect = self.client.api.exec_inspect(exec_id)
        yield "exit", inspect["ExitCode"]

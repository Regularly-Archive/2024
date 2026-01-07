from handlers.baseHandler import BaseHandler

class JupyterProjectHandler(BaseHandler):

    def define_pipeline(self):
        kernel_name = self.ctx.get_container_env('KERNEL_NAME')
        return {
            'run': f"python /nbconvert/convert.py /home/jovyan/{self.ctx.entry_point} /home/jovyan/output.txt --kernel {kernel_name}"
        }
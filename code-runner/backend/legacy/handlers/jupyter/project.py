from handlers.baseHandler import BaseHandler

class JupyterProjectHandler(BaseHandler):

    def define_pipeline(self):
        kernel_name = self.ctx.get_container_env('KERNEL_NAME')
        output_format = self.ctx.get_container_env('NBCONVERT_OUTPUT_FORMAT') or 'html'
        output_file = 'output.html' if output_format == 'html' else 'output.ipynb'
        return {
            'run': f"python /nbconvert/convert.py /home/jovyan/{self.ctx.entry_point} /home/jovyan/{output_file} --kernel {kernel_name}"
        }
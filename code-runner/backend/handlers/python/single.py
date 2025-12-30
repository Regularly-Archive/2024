from handlers.baseHandler import BaseHandler

class PythonSingleFileHandler(BaseHandler):

    def __init__(self, ctx):
        super().__init__(ctx)

        if self.ctx.dependencies:
            deps = " ".join(self.ctx.dependencies)
            super().add_pipeline('install', f"pip install {deps}")

        super().add_pipeline('run', f"python {self.ctx.entry_point}")   
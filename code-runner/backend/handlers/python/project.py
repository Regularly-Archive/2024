from handlers.baseHandler import BaseHandler
import os

class PythonProjectHandler(BaseHandler):

    def define_pipeline(self): 
        pipeline = {
            'run': f"python {self.ctx.entry_point}"
        }

        if self.ctx.project_info.has_dependencies("requirements.txt"):
            pipeline['install'] = "uv venv .venv && sh .venv/bin/activate && uv pip install -r requirements.txt" 
        
        return pipeline
from handlers.baseHandler import BaseHandler
import os

class PythonSingleFileHandler(BaseHandler):

    def define_pipeline(self): 
        pipeline = {
            'run': f"python {self.ctx.entry_point}"
        }
        
        dependencies = self.ctx.project_info.get_inline_cmd_dependencies()
        if dependencies:
            deps = " ".join(list(map(lambda x:x.name, dependencies)))
            pipeline['install'] = f"uv venv .venv && sh .venv/bin/activate && uv pip install {deps}"
        
        return pipeline
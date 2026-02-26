from handlers.baseHandler import BaseHandler
import os

class PythonSingleFileHandler(BaseHandler):

    def define_pipeline(self): 
        pipeline = {
            'run': f"uv venv && .venv/bin/python {self.ctx.entry_point}"
        }
        
        dependencies = self.ctx.project_info.get_inline_cmd_dependencies()
        if dependencies and len(dependencies) > 0:
            deps = " ".join(list(map(lambda x:x.name, dependencies)))
            pipeline['install'] = f"uv venv && uv pip install {deps}"
        
        return pipeline
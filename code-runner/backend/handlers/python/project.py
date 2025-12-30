from handlers.baseHandler import BaseHandler
import os

class PythonProjectHandler(BaseHandler):

    def define_pipeline(self): 
        pipeline = {
            'run': f"python {self.ctx.entry_point}"
        }

        if os.path.exists(os.path.join(self.ctx.project_dir, "requirements.txt")):
            pipeline['install'] = "pip install -r requirements.txt" 
        
        return pipeline
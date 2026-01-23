from handlers.baseHandler import BaseHandler

class JavaScriptProjectHandler(BaseHandler):

    def define_pipeline(self):
        pipeline = {
            'run': f"node {self.ctx.entry_point}"
        }

        if 'package.json' in self.ctx.project_info.files:
            pipeline['install'] = "npm install"
        
        return pipeline
        
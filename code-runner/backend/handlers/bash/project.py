from handlers.baseHandler import BaseHandler

class BashProjectHandler(BaseHandler):
    
    def define_pipeline(self):
        return {
            'run': f'bash {self.ctx.entry_point}'
        }
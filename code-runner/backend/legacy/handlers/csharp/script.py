from handlers.baseHandler import BaseHandler

class CSharpScriptHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'run': f"dotnet script {self.ctx.entry_point}"
        }

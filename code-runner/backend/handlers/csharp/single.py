from handlers.baseHandler import BaseHandler

class CSharpSingleFileHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'run': f"dotnet run {self.ctx.entry_point}"
        }
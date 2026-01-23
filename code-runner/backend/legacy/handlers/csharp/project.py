from handlers.baseHandler import BaseHandler

class CSharpProjectHandler(BaseHandler):
    
    def define_pipeline(self):
        return {
            'install': "dotnet restore",
            'run': "dotnet run"
        }
from handlers.baseHandler import BaseHandler

class GoModuleHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'install': f"go mod tidy",
            'run': "go run ."
        }
from handlers.baseHandler import BaseHandler

class LuaProjectHandler(BaseHandler):

    def define_pipeline(self):
        return {
            'run': f"lua {self.ctx.entry_point}"
        }
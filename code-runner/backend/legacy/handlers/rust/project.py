from handlers.baseHandler import BaseHandler

class RustProjectHandler(BaseHandler):

    def define_pipeline(self): 
        if 'Cargo.toml' in self.ctx.project_info.files:
            return {
                'build': 'cargo build',
                'run': 'cargo run'
            }
        else:
            return {
                'build': f"rustc {self.ctx.entry_point} -o main",
                'run': './main'
            }
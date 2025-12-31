class HandlerResolver:

    def resolve(self, ctx):
        files = ctx.project_info.files

        if ctx.language == "java":
            if "pom.xml" in files:
                from handlers.java.project import JavaProjectHandler
                return JavaProjectHandler(ctx)
            else:
                from handlers.java.single import JavaSingleFileHandler
                return JavaSingleFileHandler(ctx)
            
        if ctx.language == "python3":
            if "requirements.txt" in files:
                from handlers.python.project import PythonProjectHandler
                return PythonProjectHandler(ctx)
            else:
                from handlers.python.single import PythonSingleFileHandler
                return PythonSingleFileHandler(ctx)

        if ctx.language == "csharp":
            if ctx.project_info.project_form == "csharp-project":
                from handlers.csharp.project import CSharpProjectHandler
                return CSharpProjectHandler(ctx)
            elif ctx.project_info.project_form == "csharp-script":
                from handlers.csharp.script import CSharpScriptHandler
                return CSharpScriptHandler(ctx)
            else:
                from handlers.csharp.single import CSharpSingleFileHandler
                return CSharpSingleFileHandler(ctx)

        if ctx.language == "go":
            if ctx.project_info.project_form == "go-module":
                from handlers.go.project import GoModuleHandler
                return GoModuleHandler(ctx)
            else:
                from handlers.go.single import GoFileHandler
                return GoFileHandler(ctx)

        if ctx.language == "cpp":
            from handlers.cpp.project import CPPProjectHandler
            return CPPProjectHandler(ctx)

        if ctx.language == "javascript" or ctx.language == "typescript":
            from handlers.javascript.project import JavaScriptProjectHandler
            return JavaScriptProjectHandler(ctx)

        if ctx.language == "bash":
            pass

        if ctx.language == "lua":
            from handlers.lua.project import LuaProjectHandler
            return LuaProjectHandler(ctx)
        
        if ctx.language == "rust":
            from handlers.rust.project import RustProjectHandler
            return RustProjectHandler(ctx)

        raise ValueError(f"Unsupported language '{ctx.language}' or project_form '{ctx.project_form}'")

# 定义基础镜像名称
$baseImageName = "code_runner"

# 获取当前目录下的所有文件夹
$folders = Get-ChildItem -Directory

# 遍历每个文件夹
foreach ($folder in $folders) {
    # 获取文件夹名称
    $folderName = $folder.Name

    # 构建 Docker 镜像
    Write-Host "Building Docker image: $baseImageName/$folderName from ./$folderName..."

    # 使用 docker build 命令构建镜像
    docker build -t "$baseImageName/$folderName" ./$folderName

    # 检查构建是否成功
    if ($?) {
        Write-Host "Successfully built image $baseImageName/$folderName"
    } else {
        Write-Host "Failed to build image $baseImageName/$folderName"
    }
}
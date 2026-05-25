# 使用官方 .NET 运行时作为基础镜像
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# 安装 LibreOffice（PPT 转换）和 Poppler（PDF 转图片）
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    libreoffice-core \
    libreoffice-impress \
    poppler-utils \
    fonts-liberation \
    fonts-dejavu-core \
    fontconfig && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# 构建镜像
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制解决方案文件
COPY ["KnowledgeBaseService.sln", "."]
COPY ["KnowledgeBaseService.Api/KnowledgeBaseService.Api.csproj", "KnowledgeBaseService.Api/"]
COPY ["KnowledgeBaseService.Application/KnowledgeBaseService.Application.csproj", "KnowledgeBaseService.Application/"]
COPY ["KnowledgeBaseService.Core/KnowledgeBaseService.Core.csproj", "KnowledgeBaseService.Core/"]
COPY ["KnowledgeBaseService.Infrastructure/KnowledgeBaseService.Infrastructure.csproj", "KnowledgeBaseService.Infrastructure/"]

# 恢复依赖（不复制单元测试，避免 dotnet restore 报错）
RUN dotnet restore "KnowledgeBaseService.Api/KnowledgeBaseService.Api.csproj"

# 复制所有源代码
COPY . .

# 构建应用
RUN dotnet build "KnowledgeBaseService.Api/KnowledgeBaseService.Api.csproj" -c Release -o /app/build

# 发布应用
FROM build AS publish
RUN dotnet publish "KnowledgeBaseService.Api/KnowledgeBaseService.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 最终运行镜像
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 创建 PPT 转换临时目录
RUN mkdir -p /tmp/ppt-conversion && chmod 777 /tmp/ppt-conversion

# 设置环境变量
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "KnowledgeBaseService.Api.dll"]

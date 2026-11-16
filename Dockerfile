# ============================================================
# 多阶段构建：build 阶段编译，runtime 阶段只带运行时，镜像更小
# ============================================================

# ---- 阶段 1：编译 ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先拷贝 csproj 并还原，利用 Docker 层缓存（依赖没变时不重复还原）
COPY ["IndieRadar.slnx", "./"]
COPY ["src/IndieRadar.Domain/IndieRadar.Domain.csproj", "src/IndieRadar.Domain/"]
COPY ["src/IndieRadar.Application/IndieRadar.Application.csproj", "src/IndieRadar.Application/"]
COPY ["src/IndieRadar.Infrastructure/IndieRadar.Infrastructure.csproj", "src/IndieRadar.Infrastructure/"]
COPY ["src/IndieRadar.Api/IndieRadar.Api.csproj", "src/IndieRadar.Api/"]
RUN dotnet restore "src/IndieRadar.Api/IndieRadar.Api.csproj"

# 拷贝全部源码并发布
COPY . .
RUN dotnet publish "src/IndieRadar.Api/IndieRadar.Api.csproj" -c Release -o /app/publish --no-restore

# ---- 阶段 2：运行时 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# .NET 8+ 容器默认监听 8080（非 root 安全端口）
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# 启动时程序会自动建表 + 灌种子源（见 Program.cs 的 PostgresInitializer）
ENTRYPOINT ["dotnet", "IndieRadar.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Proxy-Server/Proxy-Server.csproj", "Proxy-Server/"]
RUN dotnet restore "Proxy-Server/Proxy-Server.csproj"
COPY . .
WORKDIR "/src/Proxy-Server"
RUN dotnet build "Proxy-Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Proxy-Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Proxy-Server.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Directory.Build.props carries the target framework and Directory.Packages.props
# every package version, so restore fails without them: a csproj on its own no
# longer names a framework to restore for.
COPY Payaffe.slnx global.json Directory.Build.props Directory.Packages.props ./
COPY apps/api/Payaffe.Api.csproj apps/api/
COPY src/Payaffe.Application/Payaffe.Application.csproj src/Payaffe.Application/
COPY src/Payaffe.Domain/Payaffe.Domain.csproj src/Payaffe.Domain/
COPY src/Payaffe.Infrastructure/Payaffe.Infrastructure.csproj src/Payaffe.Infrastructure/
RUN dotnet restore apps/api/Payaffe.Api.csproj

COPY . .
RUN dotnet publish apps/api/Payaffe.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Payaffe.Api.dll"]

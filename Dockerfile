# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TicTacToang.sln ./
COPY src/TicTacToang.Domain/TicTacToang.Domain.csproj src/TicTacToang.Domain/
COPY src/TicTacToang.Application/TicTacToang.Application.csproj src/TicTacToang.Application/
COPY src/TicTacToang.Infrastructure/TicTacToang.Infrastructure.csproj src/TicTacToang.Infrastructure/
COPY src/TicTacToang.Web/TicTacToang.Web.csproj src/TicTacToang.Web/
COPY tests/TicTacToang.Domain.Specs/TicTacToang.Domain.Specs.csproj tests/TicTacToang.Domain.Specs/
RUN dotnet restore TicTacToang.sln

COPY . .
RUN dotnet publish src/TicTacToang.Web/TicTacToang.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/Data/tictactoang.db"

COPY --from=build /app/publish .
RUN mkdir -p /app/Data

EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet TicTacToang.Web.dll"]

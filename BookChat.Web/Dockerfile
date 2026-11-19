# Build context must be the repository root (BookChat.sln's directory) so this stage can see
# both BookChat.Web and the BookChat.Core project it references.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY BookChat.Core/BookChat.Core.csproj BookChat.Core/
COPY BookChat.Web/BookChat.Web.csproj BookChat.Web/
RUN dotnet restore BookChat.Web/BookChat.Web.csproj

COPY BookChat.Core/ BookChat.Core/
COPY BookChat.Web/ BookChat.Web/
RUN dotnet publish BookChat.Web/BookChat.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV BookChat__DbPath=/config/bookchat.db
ENV BookChat__DownloadDirectory=/auto-add

EXPOSE 8080
ENTRYPOINT ["dotnet", "BookChat.Web.dll"]

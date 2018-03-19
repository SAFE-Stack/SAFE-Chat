FROM microsoft/aspnetcore:2.0.0-jessie
ADD ./Docker ./chat
WORKDIR ./chat/
EXPOSE 8083
ENTRYPOINT dotnet fschathost.dll --clientpath ./Client

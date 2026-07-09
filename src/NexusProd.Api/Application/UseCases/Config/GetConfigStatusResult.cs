namespace NexusProd.Api.Application.UseCases.Config;

public sealed record GetConfigStatusResult(
    bool Configured,
    string Host,
    int Port,
    string Database,
    string User,
    string Password);

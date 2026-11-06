namespace JobCopilot.Core.Matching;

/// <summary>
/// Canonical skill names with the aliases people write in CVs and JDs.
/// Extend this list as you read more job descriptions.
/// </summary>
public static class SkillCatalog
{
    public static readonly IReadOnlyDictionary<string, string[]> Skills = new Dictionary<string, string[]>
    {
        [".NET"] = [".net", "dotnet", ".net core", ".net 8", ".net 10", "asp.net core", "asp.net"],
        ["C#"] = ["c#", "csharp"],
        ["Azure"] = ["azure"],
        ["Azure Functions"] = ["azure functions", "function apps", "serverless"],
        ["Azure Service Bus"] = ["service bus", "azure service bus"],
        ["AWS"] = ["aws", "amazon web services", "lambda", "sqs"],
        ["SQL Server"] = ["sql server", "mssql", "t-sql", "tsql"],
        ["PostgreSQL"] = ["postgres", "postgresql"],
        ["SQL"] = ["sql"],
        ["Entity Framework"] = ["entity framework", "ef core"],
        ["Redis"] = ["redis"],
        ["Kafka"] = ["kafka"],
        ["RabbitMQ"] = ["rabbitmq"],
        ["Docker"] = ["docker", "containers"],
        ["Kubernetes"] = ["kubernetes", "k8s", "aks"],
        ["Terraform"] = ["terraform"],
        ["CI/CD"] = ["ci/cd", "github actions", "azure devops", "jenkins", "pipelines"],
        ["REST APIs"] = ["rest", "restful", "web api", "rest api"],
        ["Microservices"] = ["microservices", "microservice"],
        ["Event-driven"] = ["event-driven", "event driven", "messaging"],
        ["OAuth2/OIDC"] = ["oauth", "oauth2", "openid", "oidc", "jwt", "identityserver"],
        ["Security"] = ["owasp", "encryption", "security"],
        ["Observability"] = ["observability", "opentelemetry", "application insights", "app insights", "serilog", "monitoring"],
        ["Testing"] = ["unit test", "unit testing", "nunit", "xunit", "tdd", "integration test"],
        ["JavaScript"] = ["javascript"],
        ["TypeScript"] = ["typescript"],
        ["React"] = ["react", "react.js", "reactjs"],
        ["Angular"] = ["angular"],
        ["AI/LLM"] = ["llm", "openai", "genai", "generative ai", "rag", "copilot", "claude"],
        ["Agile"] = ["agile", "scrum"],
    };
}

namespace Makerverse.AppHost;

public static class BuilderExtension {

    extension<T>(IResourceBuilder<T> builder) where T : IResourceWithEnvironment {

        public IResourceBuilder<T> WithAuthCredentials(
            IResourceBuilder<ParameterResource> clientId,
            IResourceBuilder<ParameterResource> clientSecret
        ) {
            return builder.WithEnvironment("Identity__ClientId", clientId)
                .WithEnvironment("Identity__ClientSecret", clientSecret);
        }

        public IResourceBuilder<T> WithAuthentication(
            IResourceBuilder<ProjectResource> identityService,
            IResourceBuilder<ParameterResource> clientId,
            IResourceBuilder<ParameterResource> clientSecret
        ) {
            return builder.WithReference(identityService)
                .WithEnvironment("Identity__Url", identityService.GetEndpoint("http"))
                .WithEnvironment("Identity__ClientId", clientId)
                .WithEnvironment("Identity__ClientSecret", clientSecret);
        }
    }

}

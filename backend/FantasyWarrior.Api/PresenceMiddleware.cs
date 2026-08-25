using FantasyWarrior.Core.Messaging;
using FantasyWarrior.Data;

namespace FantasyWarrior.Api;

/// <summary>
/// Stamps <c>User.LastSeenUtc</c> from ordinary traffic, so presence costs no
/// heartbeat at all.
///
/// This is the whole reason the app can answer "12m ago" without a polling loop
/// keeping the Container App awake: browsing the app already proves you are
/// there. The live green dot comes from <see cref="LiveHub"/> instead; this is
/// the durable half.
/// </summary>
public static class PresenceMiddleware
{
    /// <summary>
    /// The places the API carries a username, resolved by
    /// <see cref="PresenceStamping.ResolveViewer"/> — which one of them names
    /// the *viewer* is a rule with a bug history, so it lives in Core under
    /// tests rather than inline here.
    ///
    /// The request body is deliberately not one of them: reading it would mean
    /// buffering every POST to learn something the GET traffic already tells
    /// us, and a GM who is posting is a GM who was just reading.
    /// </summary>
    public static IApplicationBuilder UsePresenceStamping(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // The hub does its own stamping on connect. Left to this middleware
            // it would stamp on *disconnect* instead, since a WebSocket's
            // next() does not return until the socket closes.
            if (context.Request.Path.StartsWithSegments("/hubs"))
            {
                await next();
                return;
            }

            await next();

            var raw = PresenceStamping.ResolveViewer(
                context.Request.Path.Value,
                context.Request.RouteValues.TryGetValue("username", out var fromRoute)
                    ? fromRoute as string
                    : null,
                context.Request.Query["username"].FirstOrDefault(),
                context.Request.Query["viewer"].FirstOrDefault());

            if (string.IsNullOrWhiteSpace(raw)) return;

            try
            {
                var db = context.RequestServices.GetRequiredService<FantasyWarriorDbContext>();
                var stamper = context.RequestServices.GetRequiredService<LastSeenStamper>();
                await stamper.StampAsync(db, Queries.Normalize(raw), DateTime.UtcNow, context.RequestAborted);
            }
            catch (Exception ex)
            {
                // Never fail a request over a presence stamp. The response has
                // already been written by this point anyway.
                context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(PresenceMiddleware))
                    .LogWarning(ex, "Could not stamp LastSeenUtc for {Username}.", raw);
            }
        });
}

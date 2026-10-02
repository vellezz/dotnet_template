using SuperApp.Cli.LocalEnvironment;

namespace SuperApp.Cli.Tests;

/// <summary>Form reading of the e2e browser on the shapes the local realm returns: the login page and the <c>form_post</c> response.</summary>
public sealed class LocalBrowserTests
{
    [Fact]
    public void Login_form_action_is_decoded_and_resolved()
    {
        var page = new BrowserResponse(200, new Uri("http://localhost:8081/realms/superapp/protocol/openid-connect/auth?x=1"), null,
            """<form id="kc-form-login" onsubmit="return true;" action="http://localhost:8081/realms/superapp/login-actions/authenticate?session_code=a&amp;tab_id=b" method="post"><input type="hidden" id="id-hidden-input" name="credentialId" value=""/></form>""");

        var form = LocalBrowser.Form(page, "kc-form-login");

        Assert.Equal("http://localhost:8081/realms/superapp/login-actions/authenticate?session_code=a&tab_id=b", form!.Value.Action.ToString());
        Assert.Equal(string.Empty, form.Value.Fields["credentialId"]);
    }

    [Fact]
    public void Form_post_response_gives_the_callback_and_its_fields()
    {
        var page = new BrowserResponse(200, new Uri("http://localhost:8081/realms/superapp/login-actions/authenticate"), null,
            """<body onload="javascript:document.forms[0].submit()"><form method="post" action="https://localhost:5001/signin-oidc"><input type="hidden" name="code" value="c&#x2d;1"/><input type="hidden" name="state" value="s"/></form></body>""");

        var form = LocalBrowser.Form(page);

        Assert.Equal(new Uri("https://localhost:5001/signin-oidc"), form!.Value.Action);
        Assert.Equal("c-1", form.Value.Fields["code"]);
        Assert.Equal("s", form.Value.Fields["state"]);
        Assert.Null(LocalBrowser.Form(page, "kc-form-login"));
    }
}

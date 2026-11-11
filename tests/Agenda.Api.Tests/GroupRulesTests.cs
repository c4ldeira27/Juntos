using Agenda.Api.Groups;

namespace Agenda.Api.Tests;

public class GroupRulesTests
{
    [Fact]
    public void Primeira_pessoa_recebe_a_primeira_cor()
    {
        Assert.Equal(GroupRules.MemberColors[0], GroupRules.NextColor([]));
    }

    [Fact]
    public void Proxima_cor_pula_as_que_ja_estao_em_uso()
    {
        var used = new[] { GroupRules.MemberColors[0] };

        Assert.Equal(GroupRules.MemberColors[1], GroupRules.NextColor(used));
    }

    [Fact]
    public void Cor_liberada_por_quem_saiu_volta_a_ser_usada()
    {
        // A pessoa da cor [0] saiu; continua só a [1]. A próxima a entrar recebe a [0].
        var used = new[] { GroupRules.MemberColors[1] };

        Assert.Equal(GroupRules.MemberColors[0], GroupRules.NextColor(used));
    }

    [Fact]
    public void Comparacao_de_cor_ignora_maiusculas()
    {
        var used = new[] { GroupRules.MemberColors[0].ToUpperInvariant() };

        Assert.Equal(GroupRules.MemberColors[1], GroupRules.NextColor(used));
    }
}

public class InviteTokensTests
{
    [Fact]
    public void Token_e_seguro_para_url_e_tem_256_bits()
    {
        var (token, _) = InviteTokens.Create();

        Assert.Equal(43, token.Length); // 32 bytes em Base64Url sem padding
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void Hash_e_deterministico_e_diferente_do_token()
    {
        var (token, hash) = InviteTokens.Create();

        Assert.Equal(hash, InviteTokens.Hash(token));
        Assert.NotEqual(token, hash);
        Assert.Equal(64, hash.Length); // SHA-256 em hex
    }

    [Fact]
    public void Tokens_gerados_nao_se_repetem()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => InviteTokens.Create().Token).ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }
}

namespace SuperRecruiter.Services;

/// <summary>
/// Test data provider for development/testing when the live site blocks requests
/// </summary>
public static class TestDataProvider
{
    public const string SampleHtml =
        @"
<!DOCTYPE html>
<html>
<body>
<table class='rating'>
    <tr><th>Character</th><th>Guild</th><th>Raid</th><th>Realm</th><th>iLvL</th><th>Updated</th></tr>
    <tr>
        <td><a href='/pug/eu/draenor/Testplayer'>blood elf paladin</a></td>
        <td>Test Guild</td>
        <td></td>
        <td>Draenor</td>
        <td>728.50</td>
        <td>Dec 18, 2025 00:00</td>
    </tr>
    <tr>
        <td><a href='/pug/eu/silvermoon/Anotherguy'>orc warrior</a></td>
        <td></td>
        <td></td>
        <td>Silvermoon</td>
        <td>725.13</td>
        <td>Dec 17, 2025 23:45</td>
    </tr>
    <tr>
        <td><a href='/pug/eu/tarrenmill/Magetest'>kul tiran mage</a></td>
        <td>Epic Gamers</td>
        <td></td>
        <td>Tarren Mill</td>
        <td>729.88</td>
        <td>Dec 17, 2025 22:30</td>
    </tr>
</table>
</body>
</html>";
}

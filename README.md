# Discord para o WigiDash (OpenWigiDashManager)

Plugin com 15 widgets que controlam o Discord aberto no PC a partir do painel WigiDash da G.SKILL, no
[OpenWigiDashManager](https://github.com/victorperez2911/OpenWigiDashManager) (.NET 10).

Esta é a branch `net10`. A `main` guarda a tentativa antiga em net472, que parou no erro 4001 do WebSocket.

## Widgets

| Widget | O que faz |
| --- | --- |
| Mute | Liga e desliga o microfone; vermelho quando mutado |
| Deafen | Liga e desliga o áudio (ensurdecer) |
| Push to Talk | Segurar abre o microfone; soltar muta |
| Push to Mute | Segurar muta; soltar volta como estava |
| Voice Input Mode | Alterna entre detecção de voz e push to talk |
| Voice Channel | Entra e sai de um canal de voz; mostra quantos estão na call |
| Text Channel | Abre um canal de texto |
| Set Audio Device | Troca o microfone e/ou a saída |
| Volume Control | Volume de entrada ou saída (no 2x1, esquerda abaixa e direita aumenta) |
| User Volume Control | Volume ou mute local de alguém da call |
| Soundboard | Toca um som do soundboard |
| Notifications | Conta as notificações; abre, limpa ou passa por elas |
| Server Stats | Membros online e total de um servidor |
| Video Toggle | Liga e desliga a câmera |
| Stream Toggle | Liga e desliga a transmissão de tela |

Todos aceitam 1x1 e 2x1 e usam uma conexão só com o Discord.

## Como fala com o Discord

Pelo named pipe local `\\.\pipe\discord-ipc-N`, o mesmo transporte do plugin do Discord da Elgato. O pipe usa
o protocolo RPC do Discord e, ao contrário do WebSocket na porta 6463, não exige Origin aprovada.

O login não usa backend nem client_secret:

1. `AUTHORIZE` pelo pipe com PKCE (S256). O Discord mostra um popup e você clica em Autorizar.
2. O código é trocado em `oauth2/token` com o `code_verifier`.
3. `AUTHENTICATE` com o token. O refresh é automático.

O token fica criptografado (DPAPI, só o seu usuário do Windows lê) em
`%LOCALAPPDATA%\OpenWigiDash\WidgetData\Discord\account.json`.

## Configuração do app no Discord

O plugin usa um app do [Developer Portal](https://discord.com/developers/applications). Os scopes `rpc.*`
só são liberados para o dono do app e para quem está no time dele.

1. Crie um app (ou use um que já seja seu).
2. Na aba **OAuth2**, ligue **Public Client** e salve.
3. No WigiDash, abra as configurações de qualquer widget do Discord, expanda **App do Developer Portal** e cole
   o Client ID.
4. Clique em **Autorizar no Discord** (ou toque no widget) e confirme no popup.

## Instalação

O plugin é publicado no nuget.org como `victorperez2911.OpenWigiDash.Discord` (pacote do tipo
`OpenWigiDashPlugin`), e o OpenWigiDashManager instala por lá.

Para instalar à mão, baixe o zip da última [release](https://github.com/victorperez2911/WigiDash-DiscordWidGet/releases) e
extraia a pasta `DiscordWidget` em `%LOCALAPPDATA%\OpenWigiDash\Widgets\`. Feche o manager pela bandeja antes
de trocar a DLL.

## Publicar uma versão

Crie a tag `v<versão>` (ex.: `v2.0.0-preview.2`) no branch `net10`. O workflow `release.yml` roda os testes,
publica o pacote no nuget.org por Trusted Publishing e anexa o zip à release do GitHub. Enquanto o SDK for
preview, a versão do plugin também precisa ser preview.

## Desenvolvimento

```powershell
dotnet test tests/DiscordWidget.Tests     # testes contra um Discord simulado no pipe
.\deploy.ps1 -RestartManager              # build e cópia para a pasta de widgets do usuário
dotnet pack -c Release                    # gera o pacote de plugin (.nupkg)
```

Para testar contra o SDK local em vez do pacote do nuget.org, compile com `-p:UseLocalOpenWigiSdk=true` e o
repositório OpenWigiDashManager clonado ao lado deste.

# Estado do projeto

**Em uma frase:** Racks organiza os ficheiros do ambiente de trabalho em painéis flutuantes no Windows.

**Estado:** 🔴 Release bloqueada pela aprovação da SignPath Foundation.

**Última atualização:** 2026-10-29, por Claude

## Onde está
- O código declara a versão 1.5.0; a release aguarda assinatura Authenticode ([procedimento](docs/RELEASING.md)).
- As instalações 1.4.0 e anteriores recebem atualizações sem verificar a assinatura do manifesto; a proteção do código atual só se aplica depois de o instalar.
- Ramo principal `main`; nada por enviar.

## Próximo passo
Aguardar a aprovação da SignPath Foundation. Depois, configurar e ativar a assinatura conforme `docs/RELEASING.md`, rever as alterações e executar as verificações locais antes de criar a tag `v1.5.0`.

## Comandos
```powershell
dotnet run --project Racks/Racks.csproj
dotnet build Racks.sln -c Release
dotnet test Racks.sln -c Release --no-build
```
A lista completa antes de publicar está em [CONTRIBUTING.md](CONTRIBUTING.md).

## Pronto quando
- A SignPath aprovar o projeto e o workflow verificar assinaturas Authenticode válidas na aplicação e no instalador.
- `dotnet build` e `dotnet test` limpos (a 2026-10-29: 1875 testes a passar) e as verificações do `CONTRIBUTING.md` concluídas, e o manifesto da atualização estiver assinado e verificado pelo procedimento de release.

## Bloqueios
- Aprovação externa da SignPath Foundation; configuração e aprovação das assinaturas pelo responsável pela release.
- Estado atual do pedido e das definições no GitHub/SignPath: por confirmar.

## Decisões
- A versão 1.5.0 será a primeira com Authenticode; não criar a tag antes da aprovação da SignPath.
- Authenticode não substitui a assinatura do manifesto: o atualizador valida o manifesto, a origem, a versão, o tamanho e o SHA-256.

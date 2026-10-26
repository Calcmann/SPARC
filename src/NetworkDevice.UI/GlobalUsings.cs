// Redireciona chamadas MessageBox para a janela padronizada SparcMessageDialog
// no mesmo estilo dark, cards e fontes UI de WanDiagnosticsWindow (modo bancada / WAN).
global using MessageBox = NetworkDevice.UI.SparcMessageDialog;

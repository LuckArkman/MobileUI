using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace LuckArkman.XR.Navigation
{
    public class DestinoSelector : MonoBehaviour
    {
        [Header("Referências")]
        public RouteManager routeManager;
        
        [Header("Interface")]
        public TMP_Dropdown dropdownDestinos;
        public Button btnIniciarNavegacao;

        private void Start()
        {
            // Garante que o banco de dados foi carregado
            if (routeManager.database == null || routeManager.database.savedRoutes.Count == 0)
            {
                routeManager.LoadDatabase();
            }

            AtualizarMenuDropdown();

            // Adiciona o evento de clique no botão
            btnIniciarNavegacao.onClick.AddListener(AoClicarIniciarRota);
        }

        private void AtualizarMenuDropdown()
        {
            dropdownDestinos.ClearOptions();
            List<string> nomesRotas = routeManager.GetAllRouteNames();

            if (nomesRotas.Count == 0)
            {
                dropdownDestinos.AddOptions(new List<string> { "Nenhum destino demarcado" });
                btnIniciarNavegacao.interactable = false;
            }
            else
            {
                dropdownDestinos.AddOptions(nomesRotas);
                btnIniciarNavegacao.interactable = true;
            }
        }

        private void AoClicarIniciarRota()
        {
            // Pega o índice do que o usuário selecionou na lista
            int indexSelecionado = dropdownDestinos.value;

            if (indexSelecionado >= 0 && indexSelecionado < routeManager.database.savedRoutes.Count)
            {
                // Pega a rota completa do banco de dados
                RouteData rotaEscolhida = routeManager.database.savedRoutes[indexSelecionado];
                
                // Entrega a rota para o seu script gerenciar
                NavigationManager.Instance.IniciarRota(rotaEscolhida);
                
                Debug.Log($"[UI] O usuário escolheu ir para: {rotaEscolhida.nomeDestino}");
                
                // Opcional: Esconder a UI do menu principal aqui
                this.gameObject.SetActive(false); 
            }
        }
    }
}
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace LuckArkman.XR.Navigation
{
    public class RouteManager : MonoBehaviour
    {
        private string filePath;
        public RouteDatabase database;

        void Awake()
        {
            // Define o caminho seguro na memória interna do celular (Android/iOS)
            filePath = Path.Combine(Application.persistentDataPath, "RotasSalvas.json");
            LoadDatabase();
        }

        // Carrega todas as rotas salvas do celular para a memória do Unity
        public void LoadDatabase()
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                database = JsonUtility.FromJson<RouteDatabase>(json);
                Debug.Log($"[RouteManager] Banco de rotas carregado! {database.savedRoutes.Count} rotas encontradas.");
            }
            else
            {
                // Se for a primeira vez abrindo o app, cria um banco vazio
                database = new RouteDatabase();
                Debug.Log("[RouteManager] Nenhum banco encontrado. Criando um novo banco vazio.");
            }
        }

        // Salva qualquer alteração no arquivo do celular
        public void SaveDatabase()
        {
            string json = JsonUtility.ToJson(database, true); // "true" deixa o texto bonito e legível
            File.WriteAllText(filePath, json);
            Debug.Log("[RouteManager] Banco de rotas salvo no celular com sucesso!");
        }

        // Função para adicionar uma nova rota finalizada
        public void AddNewRoute(RouteData newRoute)
        {
            database.savedRoutes.Add(newRoute);
            SaveDatabase();
        }

        // Função para pegar a lista de nomes para o Menu de Áudio
        public List<string> GetAllRouteNames()
        {
            List<string> names = new List<string>();
            foreach (var route in database.savedRoutes)
            {
                names.Add(route.nomeDestino);
            }
            return names;
        }
    }
}
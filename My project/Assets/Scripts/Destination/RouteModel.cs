using System;
using System.Collections.Generic;

namespace LuckArkman.XR.Navigation
{
    // Representa uma "Migalha de Pão" individual no chão
    [Serializable]
    public class RouteNode
    {
        public double latitude;
        public double longitude;

        public RouteNode(double lat, double lon)
        {
            latitude = lat;
            longitude = lon;
        }
    }

    // Representa o trajeto completo salvo (Ex: "Casa -> Padaria")
    [Serializable]
    public class RouteData
    {
        public string id;           // Um código único (ex: "rota_123456")
        public string nomeDestino;  // O nome que o sistema vai falar em áudio
        public List<RouteNode> nos; // A lista de migalhas

        public RouteData(string nome)
        {
            id = Guid.NewGuid().ToString(); // Gera um ID único automático
            nomeDestino = nome;
            nos = new List<RouteNode>();
        }
    }

    // Uma "caixa" para salvar a lista de todas as rotas no formato JSON
    [Serializable]
    public class RouteDatabase
    {
        public List<RouteData> savedRoutes = new List<RouteData>();
    }
}
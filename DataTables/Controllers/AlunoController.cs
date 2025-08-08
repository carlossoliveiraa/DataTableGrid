using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace DataTables.Controllers
{
    public class AlunoController : Controller
    {
        private readonly string _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult BuscarAlunos(int draw, int start, int length)
        {
            List<AlunoModel> alunos = new List<AlunoModel>();
            int totalRecords = 0;
            int totalRecordsFiltrados = 0;
            string termoDeBusca = Request.Form["search[value]"]; // Captura o que foi digitado na busca do DataTables

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Pegar o total geral de registros (sem filtro)
                using (SqlCommand countCmd = new SqlCommand("SELECT COUNT(*) FROM Aluno", conn))
                {
                    totalRecords = (int)countCmd.ExecuteScalar();
                }

                // Monta a consulta SQL com filtro se necessário
                string sqlBusca = @"
            SELECT Id, Nome, Email
            FROM Aluno
            WHERE (@Busca IS NULL 
                   OR Nome LIKE '%' + @Busca + '%'
                   OR Email LIKE '%' + @Busca + '%')
            ORDER BY Id
            OFFSET @Start ROWS
            FETCH NEXT @Length ROWS ONLY";

                using (SqlCommand cmd = new SqlCommand(sqlBusca, conn))
                {
                    cmd.Parameters.AddWithValue("@Busca", string.IsNullOrEmpty(termoDeBusca) ? (object)DBNull.Value : termoDeBusca);
                    cmd.Parameters.AddWithValue("@Start", start);
                    cmd.Parameters.AddWithValue("@Length", length);

                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            alunos.Add(new AlunoModel
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Nome = reader["Nome"].ToString(),
                                Email = reader["Email"].ToString()
                            });
                        }
                    }
                }

                // Pegar o total de registros filtrados (contagem aplicando o filtro)
                using (SqlCommand countFilteredCmd = new SqlCommand(@"
            SELECT COUNT(*)
            FROM Aluno
            WHERE (@Busca IS NULL 
                   OR Nome LIKE '%' + @Busca + '%'
                   OR Email LIKE '%' + @Busca + '%')", conn))
                {
                    countFilteredCmd.Parameters.AddWithValue("@Busca", string.IsNullOrEmpty(termoDeBusca) ? (object)DBNull.Value : termoDeBusca);
                    totalRecordsFiltrados = (int)countFilteredCmd.ExecuteScalar();
                }
            }

            return Json(new
            {
                draw = draw,
                recordsTotal = totalRecords,
                recordsFiltered = totalRecordsFiltrados,
                data = alunos
            }, JsonRequestBehavior.AllowGet);
        }

        public class AlunoModel
        {
            public int Id { get; set; }
            public string Nome { get; set; }
            public string Email { get; set; }
        }
    }
}

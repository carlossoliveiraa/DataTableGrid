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


******************

using Dapper;
using Newtonsoft.Json;
using Refit;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Uninter.Core.Log;
using Uninter.FichaInscricao.Domain.Repositories;
using Uninter.FichaInternacional.Domain.Dapper;
using Uninter.FichaInternacional.Domain.Dtos;
using Uninter.FichaInternacional.Domain.EntityFramework;
using Uninter.FichaInternacional.Domain.Enums;
using Uninter.FichaInternacional.Domain.Interfaces;
using Uninter.FichaInternacional.Domain.Models;
using Uninter.FichaInternacional.Domain.Models.PessoaQuinto;
using Uninter.FichaInternacional.Domain.Services;
using Uninter.FichaInternacional.Domain.Utils;
using static Uninter.FichaInternacional.Domain.Models.NotaCandidatoModel;

namespace Uninter.FichaInternacional.Domain.Repositories
{
    public partial class D5EEventoCadastroRepository
    {
        private PaymSchedRepository paymSchedRepository = new PaymSchedRepository();
        private PaymTermRepository paymTermRepository = new PaymTermRepository();
        private RecurlyService recurlyService = new RecurlyService();
        private D5EEventoFichaRepository d5EEventoFichaRepository = new D5EEventoFichaRepository();

        public int RecuperaEvento(int cdTipoCurso, int cdModalidade, int cdCurso, int cdOpcaoIngresso, int cdLocal)
        {
            List<int> arrayTipoEvento = new List<int> { 1 }; // --> PROCESSO SELETIVO EAD

            switch (cdTipoCurso)
            {
                case (int)ENivelCurso.Graduacao:
                    arrayTipoEvento.Add(4); // --> ANÁLISE DE TRANSFERÊNCIAS
                    arrayTipoEvento.Add(5); // --> REAPROVEITAMENTO DE CURSO
                    arrayTipoEvento.Add(10); // --> PROCESSO SELETIVO PROUNI

                    if (cdModalidade == 3) // --> Modalidade
                    {
                        arrayTipoEvento.Add(3); // --> PRÉ-INSCRIÇÃO ACADÊMICA - PÓS-EAD
                        arrayTipoEvento.Add(6); // --> CURSO LIVRE
                        arrayTipoEvento.Add(18); // --> PROCESSO SELETIVO MESTRADO
                    }

                    break;

                case (int)ENivelCurso.Pos:
                    arrayTipoEvento.Add(3); // --> PRÉ-INSCRIÇÃO ACADÊMICA - PÓS-EAD

                    if (cdModalidade == 3) // --> Modalidade
                    {
                        arrayTipoEvento.Add(6); // --> CURSO LIVRE
                        arrayTipoEvento.Add(21); // --> PROCESSO SELETIVO SISTEMA DE ENSINO PÓS EAD
                        arrayTipoEvento.Add(23); // --> SEMI
                    }

                    break;

                case (int)ENivelCurso.Mestrado:
                    arrayTipoEvento.Add(18); // --> SOMENTE PRESENCIAL
                    break;
            }

            using (QuintoElementoFichaInscricaoEntitiesBase context = new QuintoElementoFichaInscricaoEntitiesBase())
            {
                List<int> arrayOpcaoIngresso = new List<int>();

                var ids = (from c in context.D5ECurso
                           where c.cd_curso == cdCurso
                           && c.nome.Contains("SEGUNDA LICENCIATURA")
                           select c.cd_curso)
                           .ToList();

                if (ids.Count > 0)
                    arrayOpcaoIngresso.Add(19);

                if (cdOpcaoIngresso == 12)
                {
                    //  arrayOpcaoIngresso.Add(1);
                    arrayOpcaoIngresso.Add(12);
                }
                else if (cdOpcaoIngresso != 0)
                    arrayOpcaoIngresso.Add(cdOpcaoIngresso);

                var Evento = (from c in context.D5ECurso
                              join ec in context.D5EEventoCurso on c.cd_curso equals ec.cd_curso
                              join e in context.D5EEvento on ec.cd_evento equals e.cd_evento
                              join v in context.D5EEventoOpcaoIngressoVinculo on e.cd_evento equals v.cd_evento into left_v
                              from xv in left_v.DefaultIfEmpty()
                              where c.cd_curso == cdCurso
                              && c.cd_modalidade_curso == cdModalidade
                              && e.cd_situacao_evento == 1
                              && ec.cd_situacao_evento_curso == 1
                              && arrayTipoEvento.Contains(e.cd_tipo_evento)
                              && (arrayOpcaoIngresso.Count > 0 ? arrayOpcaoIngresso.Contains(xv.cd_opcao_ingresso) : true)
                              && ec.cd_local == cdLocal
                              && !e.nome.Contains("MIGRADO")
                              && (e.dt_inicio_inscricoes < DateTime.Now)
                              && (e.dt_termino_inscricoes > DateTime.Now)
                              orderby e.cd_evento descending
                              select new { e.cd_evento })
                              .FirstOrDefault();

                return Evento.cd_evento;
            }
        }

        public ComprovanteModel RecuperarComprovante(int cdEventoCadastro, string cpf = null)
        {
            ComprovanteModel comprovante;

            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                var query = @"
                  Select TOP 1
                        ECA.cd_tipo_operador AS CdTipoOperador,
	                    ECA.dt_inscricao as dataInscricao,
	                    'INSCRITO' as situacao,
	                    f.cd_local as cdLocal,
	                    f.cd_cidade as cdCidade,
	                    f.nome as local,
	                    f.endereco as enderecoPolo,
	                    f.fone as telefonePolo,
                        f.telefoneInternacional as telefonePoloInternacional,
	                    f.celular as celularPolo,
                        f.whatsapp as whatsAppPolo,
	                    f.horario_atend_corresp as atendimentoPolo,
                        f.observacao,
	                    CASE
		                    WHEN CURSO.cd_nivel_curso = 5 THEN evf.email_contato
		                    ELSE f.email
	                    END as emailPolo,
	                    CURSO.cd_curso as cdCurso,
	                    CURSO.nome as curso,
	                    ECA.razao as nome,
	                    ECA.cgc_cpf as cpf,
	                    decei.cidade as cidade,
	                    LOCAL.cd_estado as estadoSigla,
	                    ECA.endereco as logradouro,
	                    LOCAL.numero as numero,
	                    ECA.cep as cep,
	                    ECA.email as email,
	                    ECA.celular as telefone,
	                    ECA.cd_cadastro as ru,
	                    ECO.id_titulocli as id_titulocli,
	                    EVENTO.cd_evento as cdEvento,
	                    ECA.cd_inscricao_app as cd_inscricao_app,
	                    h.cd_modalidade_curso as cdModalidade,
	                    CURSO.cd_nivel_curso as cdNivelCurso,
	                    f.numero as numeroPolo,
	                    f.bairro as bairroPolo,
	                    f.cep as cepPolo,
                        cc.nome as cidadePolo,
                        cc.cd_estado as estadoPolo,
                        cc.cd_pais as paisPolo,
	                    ECA.cd_evento_cadastro as cdEventoCadastro,
	                    ECO.nr_opcao as nr_opcao,
	                    ECA.codigo_amigo as codigoAmigo,
	                    ECA.cd_situacao_evento_cadastro as cdSituacaoEventoCadastro,
	                    ECO.cd_situacao_evento_cadastro_opcao as cdSituacaoEventoCadastroOpcao,
	                    ECO.cd_evento_curso as cdEventoCurso,
	                    EVENTO.cd_tipo_evento as cdTipoEvento,
	                    EVENTO.cd_situacao_evento as cdSituacaoEvento,
	                    vp.codigo as codigoPromocional,
	                    tn.nome as tipoNecessidadeEspecial,
	                    ECA.necessidade_especial_outra as necessidadeEspecialOutra,
	                    ECA.nr_enem as numeroEnem,
	                    ECO.cd_aluno as cdAluno,
	                    ECA.passaporte as Passaporte,
	                    ECA.estrangeiro as Estrangeiro,
	                    ECA.endereco_internacional as EnderecoInternacional,
	                    v.url_prova_online as UrlProvaOnline,
	                    v.dt_agendamento as dataAgendamento,
                        EOIV.cd_opcao_ingresso as CdOpcaoIngresso
                    from
	                    D5EEventoCadastro ECA
                    join D5EEvento EVENTO on
	                    ECA.cd_evento = EVENTO.cd_evento
                    join D5EEventoCadastroOpcao ECO on
	                    ECA.cd_evento_cadastro = ECO.cd_evento_cadastro
                    join D5EEventoCadastroEnderecoInternacional decei on
	                    decei.cd_evento_cadastro = ECO.cd_evento_cadastro
                    join D5EEventoCurso ECU on
	                    ECO.cd_evento_curso = ECU.cd_evento_curso
                    join D5ECurso CURSO on
	                    ECU.cd_curso = CURSO.cd_curso
                    join D5ELocal f on
	                    ECU.cd_local = f.cd_local
                    join CepCidade cc on 
                        cc.cd_cidade = f.cd_cidade
                    join D5EModalidadeCurso h on
	                    CURSO.cd_modalidade_curso = h.cd_modalidade_curso
                    join D5EEventoCurso ecur on
	                    ECO.cd_evento_curso = ecur.cd_evento_curso
                    join D5ELocal LOCAL on
	                    ecur.cd_local = LOCAL.cd_local
                    left join D5EEventoCadastroAgendamento v on
	                    ECA.cd_evento_cadastro = v.cd_evento_cadastro
                    left join D5EvoucherPiloto vp on
	                    ECA.cd_voucher_piloto = vp.cd_voucher_piloto
                    left join D5ENecessidadeTipo tn on
	                    ECA.cd_tipo_necessidade_especial = tn.cd_necessidade_tipo
                    left join D5EEventoFicha evf on
	                    ECA.cd_evento = evf.cd_evento
                    left join D5EEventoOpcaoIngressoVinculo EOIV on
	                    EOIV.cd_evento = ECA.cd_evento
                    where
	                    1 = 1
                        and ECA.estrangeiro = 1 -- INTERNACIONAL
	                    and ECA.cd_evento_cadastro = @cdEventoCadastro ";

                if (!string.IsNullOrEmpty(cpf))
                    query += " and ECA.cgc_cpf = @cpf ";

                query += @" order by ECO.nr_opcao desc ";

                comprovante = conn.QueryFirstOrDefault<ComprovanteModel>(query, new { cdEventoCadastro, cpf });
            }

            if (comprovante != null)
            {
                comprovante.EventoCadastroEnderecoInternacional = GetEventoCadastroEnderecoInternacionalByCdEventoCadastro(cdEventoCadastro);
                comprovante.EventoFicha = d5EEventoFichaRepository.GetEventoFicha(comprovante.CdEvento);

                if (comprovante.CdTipoEvento == 1)
                    comprovante.AprovadoAutomaticamente = IsAprovadoAutomaticamente(cdEventoCadastro);

                return comprovante;
            }
            else
                return null;
        }

        public bool IsAprovadoAutomaticamente(int cdEventoCadastro)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                var query = @"
                SELECT
	                aprovado_automaticamente
                FROM
	                D5EEventoCadastroNota
                WHERE
	                cd_evento_cadastro = @cdEventoCadastro";

                return conn.QueryFirstOrDefault<bool>(query, new { cdEventoCadastro });
            }
        }

        public D5EEventoCadastroEnderecoInternacionalModel GetEventoCadastroEnderecoInternacionalByCdEventoCadastro(int cdEventoCadastro)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                var query = @"
                SELECT
	                cd_eventocadastro_enderecointernacional ,
	                cd_evento_cadastro ,
	                zipcode ,
	                endereco ,
	                complemento ,
	                bairro ,
	                cd_cidade ,
	                cidade ,
	                cd_estado ,
	                cd_pais ,
	                currency
                FROM
	                DTI5E.D5EEventoCadastroEnderecoInternacional
                WHERE
	                cd_evento_cadastro = @cdEventoCadastro";

                var result = conn.QueryFirstOrDefault<D5EEventoCadastroEnderecoInternacionalModel>(query, new { cdEventoCadastro });

                return result;
            }
        }

        public InscricoesModel RecuperarInscricoes(string cpf, int numeroInscricao)
        {
            if (!string.IsNullOrEmpty(cpf) && numeroInscricao > 0)
            {
                using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
                {
                    return conn.QueryFirstOrDefault<InscricoesModel>(BuscaInscricao, new { cpf, numeroInscricao });
                }
            }

            return null;
        }

        public CursoEvento BuscarCursoEvento(int? CdEvento, int CdCurso, int CdLocal, bool PortadorDiploma)
        {
            CursoEvento cursoEvento = new CursoEvento();

            var query = @"
                        select
	                        c.cd_curso,
	                        ecur.cd_evento_curso,
	                        e.cd_evento,
	                        c.cd_nivel_curso,
	                        c.cd_empresa,
	                        eoiv.cd_opcao_ingresso,
	                        e.cd_tipo_evento,
	                        e.ficha_gera_financeiro,
                            e.cd_tipo_media
                        from
	                        dti5e.d5ecurso c
                        join dti5e.d5eeventocurso ecur on
	                        ecur.cd_curso = c.cd_curso
                        join dti5e.d5eevento e on
	                        e.cd_evento = ecur.cd_evento
                        join dti5e.d5eeventomodalidadecurso emc on
	                        emc.cd_evento = e.cd_evento
                        join dti5e.d5eeventonivelcurso enc on
	                        enc.cd_evento = e.cd_evento
	                        and enc.cd_nivel_curso = c.cd_nivel_curso
                        join dti5e.D5EEventoOpcaoIngressoVinculo eoiv on
	                        eoiv.cd_evento = e.cd_evento
                        where 1 = 1
                            and e.cd_situacao_evento = 1
                            and cd_situacao_evento_curso = 1
                            and enc.cd_situacao_evento_nivel_curso = 1
                            and emc.cd_situacao_evento_modalidade_curso = 1
                            and eoiv.cd_evento_opcao_ingresso_vinculo_situ = 1
	                        and e.dt_inicio < getdate()
	                        and e.dt_termino > getdate()
	                        and c.cd_curso = @CdCurso
	                        and ecur.cd_local = @CdLocal ";

            if (PortadorDiploma)
                query += " and eoiv.cd_opcao_ingresso = 16 "; // 2ª GRADUAÇÃO

            if (CdEvento.HasValue)
                query += " and e.cd_evento = @CdEvento ";
            else
                query += " and e.DesconsiderarFichaPrincipal = 0 ";

            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                cursoEvento = conn.QueryFirstOrDefault<CursoEvento>(query, new { CdEvento, CdCurso, CdLocal });
            }

            if (cursoEvento == null)
                throw new Exception($"cursoEvento nullo para CdEvento: " + CdEvento + " CdCurso: " + CdCurso + " CdLocal: " + CdLocal);

            return cursoEvento;
        }

        public int? BuscaOpcaoIngresso(QuintoElementoFichaInscricaoEntitiesBase context, int Cd_evento, int? Cd_opcao_ingresso)
        {
            int? opcIngresso = null;

            var opcaoIngresso = (from x in context.D5EEventoOpcaoIngressoVinculo
                                 where x.cd_evento_opcao_ingresso_vinculo_situ == 1
                                 && x.cd_evento == Cd_evento
                                 && x.cd_opcao_ingresso == Cd_opcao_ingresso
                                 select x).FirstOrDefault();

            if (opcaoIngresso == null)
            {
                opcaoIngresso = (from x in context.D5EEventoOpcaoIngressoVinculo
                                 where x.cd_evento_opcao_ingresso_vinculo_situ == 1
                                 && x.cd_evento == Cd_evento
                                 select x).FirstOrDefault();
            }

            if (opcaoIngresso != null)
                opcIngresso = opcaoIngresso.cd_opcao_ingresso;

            return opcIngresso;
        }

        public int? BuscaCodigoPromocional(QuintoElementoFichaInscricaoEntitiesBase context, string CodigoPromocional, int Cd_evento)
        {
            int? codigoPromocional = null;

            codigoPromocional = (from x in context.D5EvoucherPiloto
                                 where x.codigo == CodigoPromocional
                                 && x.cd_evento == Cd_evento
                                 && x.situacao == "ATIVO"
                                 && x.dt_cadastro <= DateTime.Now
                                 && x.dt_termino >= DateTime.Now
                                 select x.cd_voucher_piloto)
                                 .FirstOrDefault();

            if (codigoPromocional == 0)
                codigoPromocional = null;

            return codigoPromocional;
        }

        public void CadastroEnderecoInternacional(int cd_evento_cadastro, D5EEventoCadastroEnderecoInternacionalModel Internacional)
        {
            try
            {
                using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
                {
                    var model = GetEventoCadastroEnderecoInternacionalByCdEventoCadastro(cd_evento_cadastro);

                    if (model == null)
                    {
                        Internacional.Cd_evento_cadastro = cd_evento_cadastro;

                        var queryCurrency = "SELECT currency from D5EFormatoZipCode dezc where iso = @Cd_pais";

                        var PaisMoeda = "";
                        switch (Internacional.CdPaisPolo) // CdPais do Polo para o ISO para Selecionar a moeda
                        {
                            case Paises.Japao: PaisMoeda = Paises.Japao_ISO3166; break;
                            case Paises.Portugal: PaisMoeda = Paises.Portugal_ISO3166; break;
                            case Paises.ReinoUnido: PaisMoeda = Paises.ReinoUnido_ISO3166; break;
                            case Paises.EstadosUnidos: PaisMoeda = Paises.EstadosUnidos_ISO3166; break;
                            case Paises.Italia: PaisMoeda = Paises.Italia_ISO3166; break;
                            case Paises.Espanha: PaisMoeda = Paises.Espanha_ISO3166; break;
                            default: PaisMoeda = Paises.EstadosUnidos_ISO3166; break;
                        }

                        Internacional.Currency = conn.QueryFirstOrDefault<string>(queryCurrency, new { Cd_pais = PaisMoeda });

                        if (string.IsNullOrEmpty(Internacional.Currency))
                            Internacional.Currency = Currency.Dolar;

                        string queryInsert = @"
                    INSERT INTO [DTI5E].[D5EEventoCadastroEnderecoInternacional]
                                                           ([cd_evento_cadastro]
                                                           ,[zipcode]
                                                           ,[endereco]
                                                           ,[complemento]
                                                           ,[bairro]
                                                           ,[cd_cidade]
                                                           ,[cidade]
                                                           ,[cd_estado]
                                                           ,[cd_pais]
                                                           ,[currency])
                                                     VALUES
                                                           (@Cd_evento_cadastro
                                                           ,@Zipcode
                                                           ,@Endereco
                                                           ,@Complemento
                                                           ,@Bairro
                                                           ,@Cd_cidade
                                                           ,@Cidade
                                                           ,@Cd_estado
                                                           ,@Cd_pais
                                                           ,@Currency)";

                        conn.Execute(queryInsert, Internacional);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public D5EEventoCadastro CriaEventoCadastro(InscricaoPessoaDto obj, CursoEvento cursoEvento, int? opcIngresso, int situacaoEventoCadastro, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            { // ESSE MÉTODO OCORRE PARA NÃO CADASTRAR DUPLICADO, POIS POR ALGUM MOTIVO ELE CHAMA ESSE MÉTODO 2 VEZES
                var quantidadePessoaPorEvento = conn.QueryFirst<int>(QuantidadePessoasPorEvento, new { EventoId = cursoEvento.Cd_evento, Cpf = obj.Pessoa.FederalId });

                if (quantidadePessoaPorEvento > 0)
                {
                    Logger.Error($"CPF duplicado: {obj.Pessoa.FederalId}", JsonConvert.SerializeObject(obj));
                    throw new Exception($"O CPF '{obj.Pessoa.FederalId}' foi cadastrado. Favor verifique seu comprovante clicando no link abaixo:");
                }
            }

            if (obj.Pessoa.Id == 0)
                throw new Exception($"Não foi possível registrar a pessoa, tente novamente.");

            Address endereco = obj.Pessoa.Contact.address.Where(x => x.IsPrimary).FirstOrDefault();

            var logradouro = $"{endereco.Street}, {endereco.Number}";

            if (logradouro.Length > 40)
                logradouro = logradouro.Substring(0, 40);

            var bairro = endereco.District;

            if (bairro.Length > 25)
                bairro = bairro.Substring(0, 25);

            var complemento = endereco.Complement;

            if (!string.IsNullOrEmpty(complemento) && complemento.Length > 20)
                complemento = complemento.Substring(0, 20);

            var eventoCadastro = new D5EEventoCadastro()
            {
                cd_evento = cursoEvento.Cd_evento,
                cd_cadastro = obj.Pessoa.Id,
                razao = obj.Pessoa.Individual.Name,
                cgc_cpf = obj.Pessoa.FederalId,
                rg = obj.Pessoa.Individual.RgNumber,
                cep = endereco.ZipCode.Trim(),
                endereco = logradouro,
                complemento = complemento,
                bairro = bairro,
                cd_estado = obj.Inscricao.Estado,
                cd_cidade = short.Parse(endereco.City.ToString()),
                fone = string.Empty,
                celular = obj.Inscricao.Fone,
                email = obj.Inscricao.Email,
                dt_nascimento = obj.Pessoa.Individual.BirthDate,
                sexo = string.Empty,
                cd_situacao_evento_cadastro = obj.Inscricao.Aprovado ? 2 : situacaoEventoCadastro,
                opcao_ingresso = opcIngresso,
                cd_inscricao_app = obj.Inscricao.CdInscricaoApp,
                funcionario = obj.Inscricao.IsColaborador,
                codigo_amigo = obj.Inscricao.CodigoAmigo,
                nome_amigo = obj.Inscricao.Nome_amigo,
                nr_enem = null,
                cd_tipo_necessidade_especial = obj.Inscricao.CdNecessidadeTipo,
                necessidade_especial_outra = obj.Inscricao.DescricaoNecessidade,
                dt_inscricao = DateTime.Now,
                cd_voucher_piloto = null,
                cd_tipo_operador = obj.Inscricao.TipoOperador,
                cd_operador = obj.Inscricao.CdOperador,
                estado_civil = "8", // --> Outros
                estrangeiro = true, // isInternacional
                endereco_internacional = obj.EnderecoInternacionalModel.EnderecoInternacional,
                telefone_valido = obj.Inscricao.TelefoneValido,
                ip_endereco = obj.Inscricao.EnderecoIp,
                cpf_valido = obj.Inscricao.CpfValido
            };

            context.D5EEventoCadastro.Add(eventoCadastro);
            context.SaveChanges();

            return eventoCadastro;
        }

        public void InsereEventoCadastroNota(EventoCadastroNota notaAnterior, int cd_evento_cadastro)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                var query = @"
                INSERT INTO [DTI5E].[D5EEventoCadastroNota]
	               (cd_evento_cadastro,
	                nota_1,
	                nota_2,
	                nota_final,
	                dt_alteracao,
	                aprovado_automaticamente)
                VALUES
                   (@cd_evento_cadastro,
                    @NotaRedacao,
                    @NotaObjetiva,
                    @NotaFinal,
                    @Data,
                    @Aprovado)";

                var model = new
                {
                    cd_evento_cadastro,
                    notaAnterior.NotaRedacao,
                    notaAnterior.NotaObjetiva,
                    notaAnterior.NotaFinal,
                    Data = DateTime.Now,
                    notaAnterior.Aprovado
                };

                conn.Execute(query, model);
            }
        }

        public void CriaEventoCadastroOpcao(D5EEventoCadastro eventoCadastro, CursoEvento cursoEvento, int nr_opcao, int? codigoPromocional, int situacaoEventoCadastro, int? CdAluno, int Nivel, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            var CdSituacaoEventoCadastroOpcao = 0;

            if (eventoCadastro.cd_situacao_evento_cadastro.HasValue)
            {
                if (eventoCadastro.cd_situacao_evento_cadastro.Value == 3)
                    CdSituacaoEventoCadastroOpcao = 2;
                else
                    CdSituacaoEventoCadastroOpcao = eventoCadastro.cd_situacao_evento_cadastro.Value;
            }
            else
                CdSituacaoEventoCadastroOpcao = situacaoEventoCadastro;

            if (nr_opcao > 1 && Nivel == (int)ENivelCurso.Pos)
                CdSituacaoEventoCadastroOpcao = 1;

            var eventoCadastroOpcao = new D5EEventoCadastroOpcao()
            {
                cd_evento_cadastro = eventoCadastro.cd_evento_cadastro,
                nr_opcao = nr_opcao,
                cd_evento_curso = cursoEvento.Cd_evento_curso,
                cd_nivel_curso = cursoEvento.Cd_nivel_curso,
                cd_situacao_evento_cadastro_opcao = CdSituacaoEventoCadastroOpcao,
                cd_empresa = cursoEvento.Cd_empresa,
                nr_serie = null,
                nr_titulo = null,
                cd_aluno = CdAluno,
                cd_voucher_piloto = codigoPromocional
            };

            try
            {
                context.D5EEventoCadastroOpcao.Add(eventoCadastroOpcao);
                context.SaveChanges();
                
                // --> Validação para garantir que o registro foi criado
                var registroCriado = context.D5EEventoCadastroOpcao
                    .Where(x => x.cd_evento_cadastro == eventoCadastro.cd_evento_cadastro && x.nr_opcao == nr_opcao)
                    .FirstOrDefault();
                    
                if (registroCriado == null)
                {
                    Logger.Error($"Falha ao criar registro D5EEventoCadastroOpcao", JsonConvert.SerializeObject(new { 
                        eventoCadastro.cd_evento_cadastro, 
                        nr_opcao, 
                        cursoEvento.Cd_evento_curso,
                        cursoEvento.Cd_nivel_curso,
                        CdSituacaoEventoCadastroOpcao
                    }));
                    throw new Exception($"Erro ao criar registro na tabela D5EEventoCadastroOpcao. Evento: {eventoCadastro.cd_evento_cadastro}, Opção: {nr_opcao}");
                }
                
                Logger.Information($"Registro D5EEventoCadastroOpcao criado com sucesso", JsonConvert.SerializeObject(new { 
                    eventoCadastro.cd_evento_cadastro, 
                    nr_opcao, 
                    cursoEvento.Cd_evento_curso 
                }));
            }
            catch (Exception ex)
            {
                Logger.Error($"Erro ao criar D5EEventoCadastroOpcao", ex, JsonConvert.SerializeObject(new { 
                    eventoCadastro.cd_evento_cadastro, 
                    nr_opcao, 
                    cursoEvento.Cd_evento_curso 
                }));
                throw;
            }
        }

        public OpcModel BuscaOpc(int cd_evento_cadastro, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            var opc = (from ec in context.D5EEventoCadastro
                       join e in context.D5EEvento on ec.cd_evento equals e.cd_evento
                       join eco in context.D5EEventoCadastroOpcao on ec.cd_evento_cadastro equals eco.cd_evento_cadastro
                       join ecur in context.D5EEventoCurso on eco.cd_evento_curso equals ecur.cd_evento_curso
                       join c in context.D5ECurso on ecur.cd_curso equals c.cd_curso
                       orderby eco.nr_opcao descending
                       where ec.cd_evento_cadastro == cd_evento_cadastro
                       select new OpcModel
                       {
                           cd_evento = e.cd_evento,
                           cd_cadastro = ec.cd_cadastro,
                           cd_empresa = eco.cd_empresa,
                           cd_evento_cadastro = ec.cd_evento_cadastro,
                           nr_plano_financeiro = eco.nr_plano_financeiro ?? 0,
                           cd_modalidade_curso = c.cd_modalidade_curso,
                           cd_nivel_curso = c.cd_nivel_curso,
                       })
                       .FirstOrDefault();

            return opc;
        }

        public AXItemModel ConfiguraItemTitulo(FinanceiroItemModel financeiroItem, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            var item = (from i in context.D5EAXItem
                        join ig in context.D5EAXItemGrupo on new { i.cd_grupo_item, i.cd_empresa } equals new { ig.cd_grupo_item, ig.cd_empresa }
                        join it in context.D5EAXItemTipo on i.cd_item_tipo equals it.cd_item_tipo
                        join itp in context.D5EAXItemTabelaPreco on new { i.cd_referencia, i.cd_empresa } equals new { itp.cd_referencia, itp.cd_empresa }
                        where i.cd_referencia == financeiroItem.referencia_item
                        && itp.cd_tabela == financeiroItem.cd_tabela_preco
                        && i.cd_empresa == financeiroItem.cd_empresa
                        && itp.cd_tabela_tipo == 2
                        select new AXItemModel
                        {
                            CdReferencia = i.cd_referencia,
                            CentroCusto = i.centro_custo,
                            Departamento = i.departamento,
                            Finalidade = i.finalidade,
                            ValorUnitario = itp.valor_unitario,
                            CdEmpresa = i.cd_empresa
                        })
                        .Distinct()
                        .First();

            return item;
        }

        public int GeraBoleto(bool IsSegundaLicenciatura, int nr_opcao, D5EEventoCadastro eventoCadastro, CursoEvento cursoEvento, InscricaoPessoaDto obj, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            bool gerarBoleto = true;
            var IdTitulo = 0;

            if (obj.Inscricao.Nivel == (int)ENivelCurso.Graduacao || obj.Inscricao.Nivel == (int)ENivelCurso.Aperfeicoamento)
            {
                if (!IsSegundaLicenciatura) // --> Segunda Licenciatura
                {
                    if (cursoEvento.Cd_tipo_evento != 4) // --> ANÁLISE DE TRANSFERÊNCIAS
                    {
                        if (obj.Inscricao.BuscarEvento) // --> Se necessita buscar as informações do evento não gera boleto
                            gerarBoleto = false; // --> Ignora a geração de Boleto
                    }
                }
            }

            if (cursoEvento.Ficha_gera_financeiro == false)
                gerarBoleto = false;

            if (gerarBoleto)
            {
                var opc = BuscaOpc(eventoCadastro.cd_evento_cadastro, context);
                
                // --> Validação para evitar NullReferenceException
                if (opc == null)
                {
                    Logger.Error($"BuscaOpc retornou null para cd_evento_cadastro: {eventoCadastro.cd_evento_cadastro}", JsonConvert.SerializeObject(new { eventoCadastro.cd_evento_cadastro, nr_opcao, cursoEvento.Cd_evento_curso }));
                    throw new Exception($"Erro ao buscar dados da opção de cadastro. Evento: {eventoCadastro.cd_evento_cadastro}, Opção: {nr_opcao}");
                }
                
                var estado = (from x in context.CEPCidade where x.cd_cidade == obj.Inscricao.CdCidade select x.cd_estado).FirstOrDefault();
                var operacao_venda = FncD5EAXOrdemVendaTipoOperacao(context, obj.Inscricao.CdCidade, estado, opc.cd_modalidade_curso, opc.cd_nivel_curso, opc.cd_empresa ?? 1);
                var descontoItem = SpcD5EEventoBuscaDescontoItem(context, cursoEvento.Cd_evento_curso, 2);

                // --> Antes de gerar efetivamente o título, verificar novamente se já não foi gerado
                var naoExisteTitulo =
                    (from x in context.D5EEventoCadastroOpcao
                     where x.cd_evento_cadastro == eventoCadastro.cd_evento_cadastro
                     && x.nr_opcao == nr_opcao
                     && x.id_titulocli != 0
                     && x.id_titulocli != null
                     select x)
                     .Count() <= 0;

                if (naoExisteTitulo)
                {
                    var financeiroItem = SpcD5EEventoBuscaFinanceiroItem(context, cursoEvento.Cd_evento_curso, 2, opc.nr_plano_financeiro);

                    if (financeiroItem == null)
                        throw new Exception($"financeiroItem spcD5EEventoBuscaFinanceiroItem");

                    // --> Verificando se existe configuração de prorrogação
                    var dataConfiguracao = new D5EEventoConfiguracaoService().DentroDoPeriodo(cursoEvento.Cd_evento, 2);

                    string dataVencimento = dataConfiguracao != null ? string.Format("{0:dd/MM/yyyy}", dataConfiguracao) : financeiroItem.dt_vencimento;
                    string strCondicaoPagamento = dataConfiguracao != null ? "D" + int.Parse(dataVencimento.Substring(0, 2)).ToString() : financeiroItem.condicao_pagamento;

                    var item = ConfiguraItemTitulo(financeiroItem, context);

                    decimal valorUnitario = 0;

                    if (item == null || item.ValorUnitario <= 0)
                        throw new Exception($"Item do curso com valor zerado! ({financeiroItem.referencia_item}). ')");
                    else if (financeiroItem.substituir_valor ?? false)
                    {
                        valorUnitario = (decimal)financeiroItem.valor;
                        item.ValorUnitario = valorUnitario;
                    }
                    else
                        valorUnitario = item.ValorUnitario;

                    if (descontoItem != null && descontoItem.porcentagem > 0)
                    {
                        valorUnitario = valorUnitario * ((100m - Convert.ToDecimal(descontoItem.porcentagem)) / 100m);
                        item.ValorUnitario = valorUnitario;
                    }

                    BankSlip bs = CriaTituloBankSlip(dataVencimento, cursoEvento, item, opc, financeiroItem, valorUnitario, strCondicaoPagamento, operacao_venda);
                    IdTitulo = Convert.ToInt32(bs.Id);
                    UpdateOpcao(nr_opcao, opc, bs, context);

                    var acc = GetConta(obj.Inscricao.Email, obj.Inscricao.Nome, eventoCadastro.cd_evento_cadastro, obj.Pessoa.Id);
                    recurlyService.CriarConta(acc);
                }
            }

            return IdTitulo;
        }
        public D5EEventoCadastroDto Save(InscricaoPessoaDto obj)
        {
            try
            {
                using (QuintoElementoFichaInscricaoEntitiesBase context = new QuintoElementoFichaInscricaoEntitiesBase())
                {
                    bool IsSegundaLicenciatura = (from c in context.D5ECurso where c.cd_curso == obj.Inscricao.Curso && c.nome.Contains("SEGUNDA LICENCIATURA") select c.cd_curso).ToList().Count > 0;
                    int situacaoEventoCadastro = 1;

                    CursoEvento cursoEvento = new CursoEvento();

                    if (obj.Inscricao.BuscarEvento)
                        cursoEvento = BuscarCursoEvento(obj.Inscricao.CdEvento, obj.Inscricao.Curso, obj.Inscricao.CdLocal, obj.Inscricao.PortadorDiploma);
                    else
                        cursoEvento = obj.Inscricao.CursoEvento;

                    if (IsSegundaLicenciatura || cursoEvento.Cd_tipo_evento == 5 || cursoEvento.Cd_tipo_evento == 4) // --> 5 = REAPROVEITAMENTO DE CURSO / 4 = ANÁLISE DE TRANSFERÊNCIAS
                        situacaoEventoCadastro = 2;

                    int? opcIngresso = BuscaOpcaoIngresso(context, cursoEvento.Cd_evento, cursoEvento.Cd_opcao_ingresso);
                    int? codigoPromocional = null;

                    if (!string.IsNullOrEmpty(obj.Inscricao.CodigoPromocional))
                        codigoPromocional = BuscaCodigoPromocional(context, obj.Inscricao.CodigoPromocional, cursoEvento.Cd_evento);

                    var eventoCadastro = new D5EEventoCadastro();
                    var nr_opcao = obj.Inscricao.CadastroOpcao ?? 1;

                    if (obj.Inscricao.CriarNovoEventoCadastro)
                    {
                        var notaAnterior = new EventoCadastroNota();

                        if (obj.Inscricao.Nivel == (int)ENivelCurso.Graduacao && cursoEvento.Cd_tipo_evento == 1)
                        {
                            notaAnterior = GetNota(obj.Pessoa.FederalId, cursoEvento.Cd_tipo_media);
                            obj.Inscricao.Aprovado = notaAnterior.Aprovado;
                        }

                        eventoCadastro = CriaEventoCadastro(obj, cursoEvento, opcIngresso, situacaoEventoCadastro, context);

                        if (notaAnterior.Aprovado)
                            InsereEventoCadastroNota(notaAnterior, eventoCadastro.cd_evento_cadastro);
                    }
                    else
                    {
                        eventoCadastro = (from x in context.D5EEventoCadastro where x.cd_evento_cadastro == obj.Inscricao.CdEventoCadastro select x).FirstOrDefault();

                        if (!obj.Inscricao.CadastroOpcao.HasValue)
                            nr_opcao = (from x in context.D5EEventoCadastroOpcao where x.cd_evento_cadastro == obj.Inscricao.CdEventoCadastro orderby x.nr_opcao descending select x.nr_opcao).FirstOrDefault();
                    }

                    if (obj.Inscricao.CriarNovoEventoCadastroOpcao)
                    {
                        // --> Verificar se o registro já existe
                        var registroExistente = context.D5EEventoCadastroOpcao
                            .Where(x => x.cd_evento_cadastro == eventoCadastro.cd_evento_cadastro && x.nr_opcao == nr_opcao)
                            .FirstOrDefault();
                            
                        if (registroExistente != null)
                        {
                            Logger.Information($"Registro D5EEventoCadastroOpcao já existe, pulando criação", JsonConvert.SerializeObject(new { 
                                eventoCadastro.cd_evento_cadastro, 
                                nr_opcao, 
                                registroExistente.cd_evento_curso
                            }));
                        }
                        else
                        {
                            nr_opcao = obj.Inscricao.CriarNovoEventoCadastro ? nr_opcao : nr_opcao + 1;

                            Logger.Information($"Criando D5EEventoCadastroOpcao", JsonConvert.SerializeObject(new { 
                                eventoCadastro.cd_evento_cadastro, 
                                nr_opcao, 
                                cursoEvento.Cd_evento_curso,
                                obj.Inscricao.CriarNovoEventoCadastro,
                                obj.Inscricao.CriarNovoEventoCadastroOpcao
                            }));

                            CriaEventoCadastroOpcao(eventoCadastro, cursoEvento, nr_opcao, codigoPromocional, situacaoEventoCadastro, obj.Inscricao.CdAluno, obj.Inscricao.Nivel, context);
                        }
                    }

                    CadastroEnderecoInternacional(eventoCadastro.cd_evento_cadastro, obj.EnderecoInternacionalModel);

                    Logger.Information($"Gerando boleto", JsonConvert.SerializeObject(new { 
                        eventoCadastro.cd_evento_cadastro, 
                        nr_opcao, 
                        cursoEvento.Cd_evento_curso,
                        IsSegundaLicenciatura
                    }));

                    var id_titulocli = GeraBoleto(IsSegundaLicenciatura, nr_opcao, eventoCadastro, cursoEvento, obj, context);

                    D5EEventoCadastroDto d5EEventoCadastroDto = new D5EEventoCadastroDto
                    {
                        CdEventoCadastro = eventoCadastro.cd_evento_cadastro,
                        CdCadastro = eventoCadastro.cd_cadastro,
                        CdEvento = eventoCadastro.cd_evento,
                        CdCurso = cursoEvento.Cd_curso.ToString(),
                        CdLocal = obj.Inscricao.CdLocal.ToString(),
                        Nome = obj.Inscricao.Nome,
                        Email = obj.Inscricao.Email,
                        NrOpcao = nr_opcao.ToString(),
                    };

                    return d5EEventoCadastroDto;
                }
            }
            catch (DbUpdateException e)
            {
                Logger.Error("DbUpdateException SAVE " + e.Message, e);

                return new D5EEventoCadastroDto { Error = true, ErrorMessage = e.Message };
            }
            catch (DbEntityValidationException e)
            {
                Logger.Error("DbEntityValidationException SAVE " + e.Message, e);

                return new D5EEventoCadastroDto { Error = true, ErrorMessage = e.Message };
            }
            catch (Exception e)
            {
                Logger.Error("Exception SAVE " + e.Message, e);

                return new D5EEventoCadastroDto { Error = true, ErrorMessage = e.Message };
            }
        }

        public BankSlip CriaTituloBankSlip(string dataVencimento, CursoEvento cursoEvento, AXItemModel item, OpcModel opc, FinanceiroItemModel financeiroItem, decimal valorUnitario, string strCondicaoPagamento, string operacao_venda)
        {
            var dtTermino = DateTime.Now;

            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                dtTermino = conn.QueryFirstOrDefault<DateTime>(BuscaDataTerminoEvento, new { cursoEvento.Cd_evento });
            }

            string dia = dataVencimento.Substring(0, 2);
            string mes = dataVencimento.Substring(3, 2);
            string ano = dataVencimento.Substring(6, 4);

            try
            {
                var bs = BankSlipPost(new BankSlipModel
                {
                    CompanyId = financeiroItem.cd_empresa,
                    ClientId = opc.cd_cadastro,
                    ContractId = null,
                    DocumentId = null,
                    PaymentModeId = "CARTEIRA",
                    OperationId = cursoEvento.Cd_nivel_curso == 1 ? "GRDEADUSA" : (cursoEvento.Cd_nivel_curso == 4 ? "POSEAD-USA" : operacao_venda),
                    Type = 2,
                    DocumentDate = DateTime.Now,
                    DueDate = new DateTime(int.Parse(ano), int.Parse(mes), int.Parse(dia)),
                    Value = valorUnitario,
                    InstallmentNumber = 1,
                    Installments = 1,
                    CashDiscountId = financeiroItem.desconto_condicional == "0" ? null : financeiroItem.desconto_condicional,
                    PaymTermId = strCondicaoPagamento,
                    PaymSchedId = "1X",
                    Message = $"Taxa de inscrição (Evento: {opc.cd_evento} Cód: inscrição: {opc.cd_evento_cadastro}) Pré-matrícula [ficha] (Evento: {opc.cd_evento} Cód: inscrição: {opc.cd_evento_cadastro})",
                    FinancialAgreement = null,
                    Items = CreateFinancialTitle(item),
                    InvoiceNow = false,
                    State = 1,
                    FinancingId = 0,
                    IsSerasaAllowed = false,
                    InvoiceText = "",
                    BillableDate = dtTermino
                });

                return bs;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void UpdateOpcao(int nr_opcao, OpcModel opc, BankSlip bs, QuintoElementoFichaInscricaoEntitiesBase context)
        {
            var updOpcao = (from x in context.D5EEventoCadastroOpcao
                            where x.cd_evento_cadastro == opc.cd_evento_cadastro
                            && x.nr_opcao == nr_opcao
                            select x)
                            .FirstOrDefault();

            if (updOpcao != null)
            {
                updOpcao.cd_empresa = bs.CompanyId;
                updOpcao.nr_titulo = bs.DocumentId;
                updOpcao.id_titulocli = Convert.ToInt32(bs.Id);
                context.Entry(updOpcao).State = EntityState.Modified;
                context.SaveChanges();
            }
        }

        private ContaInternacional GetConta(string email, string nome, int cdEventoCadastro, int cdCadastro)
        {
            if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(nome))
            {
                var nameTrim = nome.Trim();
                var names = nameTrim.Split(' ');
                var firstName = names[0];
                var lastName = names.Length > 1 ? nameTrim.Replace(firstName, "").Trim() : "";

                return new ContaInternacional(firstName, lastName, email, cdCadastro.ToString());
            }
            else
            {
                Logger.Error("SISPAP - Nome ou Email em Branco", JsonConvert.SerializeObject(new { nome, email, cdEventoCadastro }));

                return null;
            }
        }

        [DbFunction("DTI5E", "fncD5EAXOrdemVendaTipoOperacao")]
        private string FncD5EAXOrdemVendaTipoOperacao(DbContext contexto, int? cd_cidade, string cd_estado, int cd_modalidade_curso, int cd_nivel_curso, int? cd_empresa)
        {
            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("cd_cidade", cd_cidade ?? (object)DBNull.Value),
                new SqlParameter("cd_estado", cd_estado ?? (object)DBNull.Value),
                new SqlParameter("cd_modalidade_curso", cd_modalidade_curso),
                new SqlParameter("cd_nivel_curso", cd_nivel_curso),
                new SqlParameter("cd_empresa", cd_empresa ?? (object)DBNull.Value)
            };

            var output = contexto.Database.
                    SqlQuery<string>("SELECT DTI5E.fncD5EAXOrdemVendaTipoOperacao(@cd_cidade, @cd_estado, @cd_modalidade_curso, @cd_nivel_curso, @cd_empresa)", parameters.ToArray())
                .FirstOrDefault();

            return output;
        }

        private FinanceiroItemModel SpcD5EEventoBuscaFinanceiroItem(DbContext contexto, int cd_evento_curso, int cd_evento_tipo_trans, int? cd_evento_financeiro)
        {
            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("cd_evento_curso", cd_evento_curso),
                new SqlParameter("cd_evento_tipo_trans", cd_evento_tipo_trans),
                new SqlParameter("cd_evento_financeiro", cd_evento_financeiro ?? (object)DBNull.Value)
            };

            var procedureRet = contexto.Database.SqlQuery<FinanceiroItemModel>("EXEC spcD5EEventoBuscaFinanceiroItem @cd_evento_curso, @cd_evento_tipo_trans, @cd_evento_financeiro", parameters.ToArray()).FirstOrDefault();

            if (procedureRet != null)
            {
                var hasPayTerm = paymTermRepository.HasPaymTerm(procedureRet.condicao_pagamento, procedureRet.cd_empresa);
                if (hasPayTerm == false)
                    return null;

                var numeroParcela = paymSchedRepository.GetNumOfPaymentsByCode(procedureRet.plano_pagamento, procedureRet.cd_empresa);
                if (numeroParcela == null)
                    return null;
                else
                    procedureRet.numero_parcela = numeroParcela.Value;
            }

            return procedureRet;
        }

        private DescontoItemModel SpcD5EEventoBuscaDescontoItem(DbContext contexto, int cd_evento_curso, int cd_evento_tipo_trans)
        {
            List<SqlParameter> parameters = new List<SqlParameter>
            {
                new SqlParameter("cd_evento_curso", cd_evento_curso),
                new SqlParameter("cd_evento_tipo_trans", cd_evento_tipo_trans)
            };

            return contexto.Database.
                SqlQuery<DescontoItemModel>("EXEC spcD5EEventoBuscaDescontoItem @cd_evento_curso, @cd_evento_tipo_trans", parameters.ToArray()).FirstOrDefault();
        }

        private List<BankSlipItemModel> CreateFinancialTitle(AXItemModel item)
        {
            List<BankSlipItemModel> BankSlipItems = new List<BankSlipItemModel>
            {
                new BankSlipItemModel
                {
                    Id = item.CdReferencia,
                    Quantity = item.CdEmpresa,
                    UnitCost = item.ValorUnitario,
                    Department = item.Departamento,
                    CostCenter = item.CentroCusto,
                    Finality = item.Finalidade,
                    Discounts = new List<BankSlipDiscountModel>()
                }
            };

            return BankSlipItems;
        }

        private BankSlip BankSlipPost(BankSlipModel bankslip)
        {
            string result = Post(bankslip, EnviromentVariables.API_BankSlip + "api/bankslip", "Matricula");

            if (result == "")
                return null;

            result = result.Replace("{\"bankSlip\":", "");
            result = result.Remove(result.Length - 1);

            JavaScriptSerializer jss = new JavaScriptSerializer();
            BankSlip bs = jss.Deserialize<BankSlip>(result);

            return bs;
        }

        private string Post(object obj, string address, string urlSispap)
        {
            try
            {
                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(address);
                webRequest.Method = "POST";
                webRequest.ContentType = "application/json";

                webRequest.Headers.Add("Authorization", "bearer " + GetBearerToken());

                string json = JsonConvert.SerializeObject(obj);

                using (var streamWriter = new StreamWriter(webRequest.GetRequestStream()))
                {
                    streamWriter.Write(json);
                }

                webRequest.Timeout = 30000;
                string req = webRequest.ToRaw(json);

                HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse();
                Stream stream = webResponse.GetResponseStream();
                StreamReader streamReader = new StreamReader(stream);

                string result = streamReader.ReadToEnd();

                ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => false;

                SalvaApiLog(req, result, (int)webResponse.StatusCode, urlSispap);

                SalvaTituloCliLog(urlSispap, req, ((BankSlipModel)obj).CompanyId.Value, ((BankSlipModel)obj).ClientId, result, (int)webResponse.StatusCode);

                if (webResponse.StatusCode == HttpStatusCode.Created)
                    return result;
                else
                    return "";
            }
            catch (WebException ex)
            {
                using (WebResponse webResponse = ex.Response)
                {
                    HttpWebResponse httpResponse = (HttpWebResponse)webResponse;
                    string json = null;
                    if (httpResponse != null)
                    {
                        Stream stream = webResponse.GetResponseStream();
                        StreamReader streamReader = new StreamReader(stream);
                        json = streamReader.ReadToEnd();
                    }

                    try
                    {
                        JsonErrorPattern jsonErrorPattern = JsonConvert.DeserializeObject<JsonErrorPattern>(json);
                        if (jsonErrorPattern.Debug == null)
                            jsonErrorPattern.Debug = GetExceptionLastMessage(ex);

                        throw new Exception(jsonErrorPattern.Message);
                    }
                    catch (Exception e)
                    {
                        throw new Exception(GetExceptionLastMessage(e));
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string GetBearerToken()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(EnviromentVariables.API_Registry_Token);

                string body = "username=" + EnviromentVariables.AuthApiUsername
                    + "&password=" + EnviromentVariables.AuthApiPassword
                    + "&grant_type=" + EnviromentVariables.AuthApiGrantType
                    + "&client_id=" + EnviromentVariables.AuthApiClientId
                    + "&client_secret=" + EnviromentVariables.AuthApiClientSecret;

                byte[] bodyStream = Encoding.UTF8.GetBytes(body);

                request.ContentType = EnviromentVariables.ContentTypeAuth;
                request.Method = "POST";
                request.ContentLength = bodyStream.Length;

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(AcceptAllCertifications);

                Stream newStream = request.GetRequestStream();
                newStream.Write(bodyStream, 0, bodyStream.Length);
                newStream.Close();

                WebResponse webResponse = request.GetResponse();
                WebResponse resp = request.GetResponse();

                if (resp == null)
                    return null;

                StreamReader sr = new StreamReader(resp.GetResponseStream());

                var result = System.Web.Helpers.Json.Decode(sr.ReadToEnd().Trim());
                return result.access_token;
            }
            catch (WebException ex)
            {
                using (WebResponse webResponse = ex.Response)
                {
                    HttpWebResponse httpResponse = (HttpWebResponse)webResponse;
                    string json = null;
                    if (httpResponse != null)
                    {
                        Stream stream = webResponse.GetResponseStream();
                        StreamReader streamReader = new StreamReader(stream);
                        json = streamReader.ReadToEnd();
                    }

                    try
                    {
                        JsonErrorPattern jsonErrorPattern = JsonConvert.DeserializeObject<JsonErrorPattern>(json);
                        if (jsonErrorPattern.Debug == null)
                            jsonErrorPattern.Debug = GetExceptionLastMessage(ex);

                        throw new Exception(jsonErrorPattern.Message);
                    }
                    catch (Exception e)
                    {
                        throw new Exception(GetExceptionLastMessage(e) + " | " + json);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(GetExceptionLastMessage(ex));
            }
        }

        public string GetBearerTokenV4(string username)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(AcceptAllCertifications);

                var oauthApi = RestService.For<ITokenApiService>(EnviromentVariables.TokenApi);
                var tokenApi = Task.Run(() => oauthApi.GetToken(new TokenApi { UserName = username })).Result;
                var jsonToken = JsonConvert.SerializeObject(tokenApi.data);
                var serializer = new JavaScriptSerializer();
                var tokenData = serializer.Deserialize<TokenData>(jsonToken);

                return tokenData.Token;
            }
            catch (Exception ex)
            {
                throw new Exception(GetExceptionLastMessage(ex));
            }
        }

        private static string GetExceptionLastMessage(Exception exception)
        {
            string debug = exception.Message;
            while (exception.InnerException != null)
            {
                debug = exception.InnerException.Message;
                exception = exception.InnerException;
            }

            return debug;
        }

        private bool AcceptAllCertifications
        (
            object sender,
            System.Security.Cryptography.X509Certificates.X509Certificate certification,
            System.Security.Cryptography.X509Certificates.X509Chain chain,
            System.Net.Security.SslPolicyErrors sslPolicyErrors
        )
        {
            try
            {
                if (sslPolicyErrors != null)
                {
                    Logger.Error($"Uninter.FichaInternacional - Erro de validacao SSL - AcceptAllCertifications", JsonConvert.SerializeObject(new { sender, sslPolicyErrors, certification, chain }));
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Uninter.FichaDeInscricao - Erro log AcceptAllCertifications", ex);
            }
            return true;
        }

        private void SalvaTituloCliLog(string urlSispap, string request, int cdEmpresa, int cdCadastroMensalidade, string response, int codigo)
        {
            using (var contexto = new QuintoElementoFichaInscricaoEntitiesBase())
            {
                StringBuilder query = new StringBuilder();
                List<SqlParameter> ps = new List<SqlParameter>();

                string src = "FIchaInscricao: Matricula";
                string responseTituloCli;

                // se codigo == 200 ou 201, sucesso = 1, mensagem vazia, senao, mensagem com erro, sucesso vazio
                if (codigo == 200 || codigo == 201)
                {
                    var aux = response.Replace("{\"bankSlip\":", "");
                    aux = aux.Remove(aux.Length - 1);

                    JavaScriptSerializer jss = new JavaScriptSerializer();
                    BankSlip bs = jss.Deserialize<BankSlip>(aux);

                    responseTituloCli = "IntegracaoRow Object\r\n(\r\n\t[offset:protected] => \r\n\t[codigo] => " + codigo + "\r\n\t[mensagem] => \r\n\t[nr_titulo] => " + bs.DocumentId + "\r\n\t[sucesso] => 1\r\n)";
                }
                else
                    responseTituloCli = "IntegracaoRow Object\r\n(\r\n\t[offset:protected] => \r\n\t[codigo] => " + codigo + "\r\n\t[mensagem] => " + response + "\r\n\t[sucesso] => \r\n)";

                try
                {
                    contexto.D5ETituloCliLog.Add(new D5ETituloCliLog()
                    {
                        cd_cadastro_resp = null,
                        origem = src,
                        dt_registro = DateTime.Now,
                        pay_load = request,
                        cd_tipo_transacao = urlSispap.Equals("Matricula") ? 2 : 3,
                        cd_empresa = cdEmpresa,
                        cd_cadastro = cdCadastroMensalidade,
                        metodo = urlSispap,
                        retorno = responseTituloCli
                    });

                    contexto.SaveChanges();
                }
                catch (Exception ex)
                {
                    Logger.Error("Erro SalvaTituloCliLog - " + ex.Message, ex);

                    throw new Exception("Erro no método 'SalvaTituloCliLog' | " + GetExceptionLastMessage(ex));
                }
            }
        }

        private void SalvaApiLog(string request, string response, int codigo, string urlSispap)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                IDbTransaction trans = conn.BeginTransaction();

                try
                {
                    conn.Execute(SalvarLogAPI,
                    new
                    {
                        Header = request,
                        Result = response,
                        Code = codigo,
                        CreateDate = DateTime.Now,
                        UserId = $"FichaInscricao: Matricula/{urlSispap}"
                    }, trans);

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    trans.Rollback();
                    throw new Exception("Erro no método 'SalvaApiLog' | " + GetExceptionLastMessage(ex));
                }
            }
        }

        public EventoCadastroNota GetNota(string cpf, int cdTipoMedia)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(AcceptAllCertifications);

                var nota = RestService.For<ITokenApiService>(EnviromentVariables.QuintoElementoApi);
                var token = "Bearer " + GetBearerTokenV4("SistemasInternos");
                var notaCandidato = Task.Run(() => nota.GetNota(cpf, cdTipoMedia, token)).Result;

                return new EventoCadastroNota()
                {
                    Aprovado = notaCandidato.candidatoEad.aprovado,
                    NotaRedacao = notaCandidato.candidatoEad.nota_redacao_ead,
                    NotaObjetiva = notaCandidato.candidatoEad.nota_objetiva_ead,
                    NotaFinal = notaCandidato.candidatoEad.nota_final_ead
                };
            }
            catch (Exception ex)
            {
                Logger.Error("Ficha Inscricao - GetNota", ex, cpf);
                return new EventoCadastroNota() { Aprovado = false };
            }
        }

        public bool VerificaRU(string ru)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                var retorno = conn.Query<int>(CountRU, new { Ru = ru }).FirstOrDefault();

                return retorno > 0;
            }
        }

        public bool PodeRealizarProvaOnline(int cdEvento)
        {
            using (var conn = ConnectionFactory.GetOpenConnection5Elemento())
            {
                try
                {
                    var retorno = conn.Query<DateTime>(DataEvento, new { cdEvento }).FirstOrDefault();

                    return retorno >= DateTime.Now;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
}

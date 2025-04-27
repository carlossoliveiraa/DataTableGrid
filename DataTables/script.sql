CREATE TABLE Aluno (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Nome NVARCHAR(100),
    Email NVARCHAR(100)
)

-- Gerar 4000 alunos com muito mais variedade de nomes e sobrenomes
SET NOCOUNT ON;

DECLARE @Nomes TABLE (Nome NVARCHAR(50));
DECLARE @Sobrenomes TABLE (Sobrenome NVARCHAR(50));

-- Inserindo muitos nomes
INSERT INTO @Nomes (Nome)
VALUES 
('Ana'), ('Bruno'), ('Carlos'), ('Daniela'), ('Eduardo'), ('Fernanda'), ('Gabriel'), ('Helena'), 
('Igor'), ('Juliana'), ('Kleber'), ('Larissa'), ('Marcelo'), ('Natália'), ('Otávio'), ('Paula'),
('Quésia'), ('Rafael'), ('Simone'), ('Tiago'), ('Ursula'), ('Victor'), ('Wesley'), ('Xuxa'), 
('Yasmin'), ('Zeca'), ('Amanda'), ('Beatriz'), ('Caio'), ('Débora'), ('Enzo'), ('Flávia'), 
('Gustavo'), ('Heloísa'), ('Isabela'), ('João'), ('Karen'), ('Leonardo'), ('Manuela'), ('Nicole'),
('Pedro'), ('Renan'), ('Sabrina'), ('Tatiane'), ('Vinícius'), ('William'), ('Bianca'), ('Danilo');

-- Inserindo muitos sobrenomes
INSERT INTO @Sobrenomes (Sobrenome)
VALUES 
('Silva'), ('Souza'), ('Oliveira'), ('Santos'), ('Pereira'), ('Almeida'), ('Ferreira'), 
('Rodrigues'), ('Costa'), ('Martins'), ('Gomes'), ('Barros'), ('Carvalho'), ('Moura'), 
('Freitas'), ('Teixeira'), ('Azevedo'), ('Ramos'), ('Campos'), ('Peixoto'), ('Dias'), ('Batista'), 
('Vieira'), ('Nogueira'), ('Rocha'), ('Mendes'), ('Farias'), ('Castro'), ('Moreira'), ('Pinheiro');

DECLARE @i INT = 1;

WHILE @i <= 5000
BEGIN
    DECLARE @NomeEscolhido NVARCHAR(50);
    DECLARE @Sobrenome1 NVARCHAR(50);
    DECLARE @Sobrenome2 NVARCHAR(50);
    DECLARE @NomeCompleto NVARCHAR(150);

    -- Selecionar nome e sobrenomes aleatórios
    SELECT TOP 1 @NomeEscolhido = Nome FROM @Nomes ORDER BY NEWID();
    SELECT TOP 1 @Sobrenome1 = Sobrenome FROM @Sobrenomes ORDER BY NEWID();
    SELECT TOP 1 @Sobrenome2 = Sobrenome FROM @Sobrenomes ORDER BY NEWID();

    -- Montar Nome Completo (às vezes só um sobrenome, às vezes dois)
    IF (@i % 2 = 0) -- se par, coloca dois sobrenomes
        SET @NomeCompleto = CONCAT(@NomeEscolhido, ' ', @Sobrenome1, ' ', @Sobrenome2);
    ELSE -- se ímpar, só um sobrenome
        SET @NomeCompleto = CONCAT(@NomeEscolhido, ' ', @Sobrenome1);

    -- Inserir aluno
    INSERT INTO Aluno (Nome, Email)
    VALUES (
        @NomeCompleto,
        LOWER(CONCAT(REPLACE(@NomeEscolhido,' ',''),
                     '.',
                     REPLACE(@Sobrenome1,' ',''),
                     @i,
                     '@email.com'))
    );

    SET @i = @i + 1;
END

 
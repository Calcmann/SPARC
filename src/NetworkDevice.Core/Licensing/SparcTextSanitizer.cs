using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Licensing;

/// <summary>
/// Padronizador unificado de formatação de caixas de texto para cadastros e licenças SPARC.
/// Garante consistência de caixa (Title Case, UPPERCASE, lowercase e máscaras) em todas as plataformas.
/// </summary>
public static class SparcTextSanitizer
{
    private static readonly HashSet<string> LowercasePrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "da", "do", "das", "dos", "e"
    };

    private static readonly HashSet<string> UppercaseAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "SA", "S/A", "S.A.", "S.A", "LTDA", "ME", "EPP", "TI", "SP", "RJ", "MG", "PR", "SC", "RS",
        "BA", "PE", "CE", "GO", "DF", "ES", "MT", "MS", "PA", "AM", "RN", "PB", "AL", "SE", "PI", "MA", "TO", "RO", "AC", "AP", "RR"
    };

    /// <summary>
    /// Padroniza nomes de pessoas e empresas para Title Case, preservando preposições e siglas.
    /// Ex.: "JOAO DA SILVA" -> "João da Silva", "claro brasil sa" -> "Claro Brasil SA".
    /// </summary>
    public static string FormatPersonOrCompanyName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var tokens = Regex.Split(input.Trim(), @"\s+");
        var sb = new StringBuilder();

        for (int i = 0; i < tokens.Length; i++)
        {
            var word = tokens[i].Trim();
            if (string.IsNullOrEmpty(word)) continue;

            if (sb.Length > 0) sb.Append(' ');

            if (UppercaseAcronyms.Contains(word))
            {
                sb.Append(word.ToUpperInvariant());
            }
            else if (i > 0 && LowercasePrepositions.Contains(word))
            {
                sb.Append(word.ToLowerInvariant());
            }
            else
            {
                // Primeira letra maiúscula, restante minúscula
                if (word.Length == 1)
                {
                    sb.Append(char.ToUpperInvariant(word[0]));
                }
                else
                {
                    sb.Append(char.ToUpperInvariant(word[0]));
                    sb.Append(word.Substring(1).ToLowerInvariant());
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Padroniza e-mails para minúsculas limpas.
    /// Ex.: " JOAO.SILVA@CLARO.COM.BR " -> "joao.silva@claro.com.br".
    /// </summary>
    public static string FormatEmail(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Padroniza identificação de matrícula para MAIÚSCULAS limpas sem espaços supérfluos.
    /// Ex.: " t123456 " -> "T123456".
    /// </summary>
    public static string FormatEmployeeId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Padroniza o Cluster de atuação (ex.: "sp interior" -> "SP Interior", "rio grande sp" -> "Rio Grande SP").
    /// </summary>
    public static string FormatCluster(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var parts = Regex.Split(input.Trim(), @"(\s+|[-/])");
        var sb = new StringBuilder();

        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (p == " " || p == "-" || p == "/")
            {
                sb.Append(p);
                continue;
            }

            if (UppercaseAcronyms.Contains(p) || p.Length <= 3)
            {
                sb.Append(p.ToUpperInvariant());
            }
            else if (LowercasePrepositions.Contains(p))
            {
                sb.Append(p.ToLowerInvariant());
            }
            else
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                sb.Append(p.Substring(1).ToLowerInvariant());
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Padroniza a UF com 2 letras maiúsculas.
    /// Ex.: "sp" -> "SP".
    /// </summary>
    public static string FormatUf(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var clean = Regex.Replace(input.Trim(), @"[^a-zA-Z]", "").ToUpperInvariant();
        return clean.Length > 2 ? clean.Substring(0, 2) : clean;
    }

    /// <summary>
    /// Padroniza número de telefone / WhatsApp com formatação unificada (XX) XXXXX-XXXX ou (XX) XXXX-XXXX.
    /// Se não corresponder a 10 ou 11 dígitos, retorna os dígitos limpos.
    /// </summary>
    public static string FormatPhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var digits = Regex.Replace(input.Trim(), @"\D", "");
        if (digits.Length == 11)
        {
            return $"({digits.Substring(0, 2)}) {digits.Substring(2, 5)}-{digits.Substring(7, 4)}";
        }
        if (digits.Length == 10)
        {
            return $"({digits.Substring(0, 2)}) {digits.Substring(2, 4)}-{digits.Substring(6, 4)}";
        }

        return digits.Length > 0 ? digits : input.Trim();
    }
}

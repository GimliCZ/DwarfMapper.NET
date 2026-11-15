// SPDX-License-Identifier: GPL-2.0-only

// 02 - Name the pairs on the union map itself. A [MapDerivedType] arm says which target case a source case becomes;
// the pair it names is then mapped like any nested pair (here member by member), with no method of its own. A case
// both unions share needs no arm at all: Ticket passes through as the same instance.

namespace DwarfMapper.Unions.Ex02
{
    public record class Ticket(string Code);

    public record class Email(string Address, string Subject);

    public record class Sms(string Number, string Text);

    public record class EmailView(string Address, string Subject);

    public record class SmsView(string Number, string Text);

    public union Message(Email, Sms, Ticket);

    public union MessageView(EmailView, SmsView, Ticket);

// <snippet: union-explicit-arms>
    [DwarfMapper]
    public partial class MessageMapper
    {
        [MapDerivedType<Email, EmailView>]
        [MapDerivedType<Sms, SmsView>]
        public partial MessageView ToView(Message message); // Ticket: the same type in both unions, passed through
    }
// </snippet>

    public static class Example
    {
        public static string Run()
        {
            var mapper = new MessageMapper();
            var ticket = new Ticket("T-1");
            var email = mapper.ToView(new Email("ann@example.com", "Hello")).Value;
            var sms = mapper.ToView(new Sms("+420", "Hi")).Value;
            var passed = ReferenceEquals(mapper.ToView(ticket).Value, ticket) ? "same Ticket" : "a copy";
            return email + " | " + sms + " | " + passed;
        }

        public const string Expected = "EmailView { Address = ann@example.com, Subject = Hello } | SmsView { Number = +420, Text = Hi } | same Ticket";
    }
}

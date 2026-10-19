import { type FormEvent, useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Field } from '../../components/Field';
import { toErrorMessage } from '../../api/client';
import { useAuth } from './useAuth';

interface LoginLocationState {
  from?: string;
}

export function LoginPage() {
  const { session, login } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);

  if (session) {
    return <Navigate to="/tickets" replace />;
  }

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const nextErrors = {
      email: email.trim() ? undefined : 'Enter your email address.',
      password: password ? undefined : 'Enter your password.',
    };
    setErrors(nextErrors);
    setSubmitError(undefined);

    if (nextErrors.email || nextErrors.password) {
      return;
    }

    setIsSubmitting(true);
    try {
      await login(email.trim(), password);
      const state = location.state as LoginLocationState | null;
      navigate(state?.from ?? '/tickets', { replace: true });
    } catch (error) {
      setSubmitError(toErrorMessage(error, 'Unable to sign in.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <main className="login-page">
      <section className="login-card" aria-labelledby="login-title">
        <p className="login-card__eyebrow">Northwind Support</p>
        <h1 id="login-title">Agent sign in</h1>
        <p className="login-card__intro">Use your support agent account to continue.</p>

        <form onSubmit={handleSubmit} noValidate>
          <Field id="email" label="Email address" error={errors.email}>
            {(props) => (
              <input
                {...props}
                type="email"
                autoComplete="username"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            )}
          </Field>

          <Field id="password" label="Password" error={errors.password}>
            {(props) => (
              <input
                {...props}
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            )}
          </Field>

          {submitError && (
            <p className="login-card__error" role="alert">
              {submitError}
            </p>
          )}

          <button className="button button--primary login-card__submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </section>
    </main>
  );
}
